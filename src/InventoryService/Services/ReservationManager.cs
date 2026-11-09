using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Domain;
using GestionCommandes.InventoryService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.InventoryService.Services;

/// <summary>
/// Reservations de stock : c'est le participant de la saga de commande.
///
/// Trois operations, qui correspondent exactement aux trois moments du cycle de vie :
/// <list type="number">
///   <item><b>Reserver</b> a la confirmation de la commande,</item>
///   <item><b>Liberer</b> en cas d'annulation ou de compensation de saga,</item>
///   <item><b>Consommer</b> a l'expedition : la reservation devient une sortie definitive.</item>
/// </list>
///
/// Les trois sont <b>idempotentes</b> sur la reference (le numero de commande) :
/// un rejeu de l'appel ne double jamais l'effet.
/// </summary>
public sealed class ReservationManager(
    InventoryDbContext context,
    StockLedger ledger,
    ICurrentUser currentUser,
    ILogger<ReservationManager> logger)
{
    /// <summary>Nombre de tentatives en cas de conflit de concurrence optimiste.</summary>
    private const int MaxConcurrencyAttempts = 4;

    /// <summary>
    /// Reserve le stock necessaire a une commande.
    ///
    /// La disponibilite est verifiee pour <b>toutes</b> les lignes avant d'ecrire
    /// quoi que ce soit : soit la reservation passe en entier, soit elle est refusee
    /// en 409 avec le detail des manques (pas de reservation partielle).
    ///
    /// Le verrou optimiste sur la ligne de stock empeche la survente : si deux
    /// commandes reservent le meme produit en meme temps, la seconde est rejouee
    /// sur des valeurs a jour.
    /// </summary>
    public async Task<(ReservationResponse Reservation, bool AlreadyExisted)> ReserveAsync(
        ReservationRequest request,
        CancellationToken cancellationToken)
    {
        var reference = request.Reference.Trim();

        for (var attempt = 1; attempt <= MaxConcurrencyAttempts; attempt++)
        {
            try
            {
                return await TryReserveAsync(reference, request, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                context.ChangeTracker.Clear();

                logger.LogWarning(
                    "Conflit de concurrence sur la reservation {Reference} (tentative {Attempt}/{Max})",
                    reference,
                    attempt,
                    MaxConcurrencyAttempts);

                if (attempt == MaxConcurrencyAttempts)
                {
                    throw new ConflictException(
                        "Le stock de ces produits est modifie par une autre operation. " +
                        "Merci de reessayer dans quelques instants.",
                        "CONCURRENCY_CONFLICT",
                        new Dictionary<string, object?> { ["attempts"] = attempt });
                }

                // Petite attente croissante avant de rejouer sur des valeurs fraiches.
                await Task.Delay(40 * attempt, cancellationToken);
                _ = ex;
            }
        }

        throw new UnreachableException();
    }

    private async Task<(ReservationResponse, bool)> TryReserveAsync(
        string reference,
        ReservationRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await context.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == reference, cancellationToken);

        if (existing is { Status: ReservationStatus.Active })
        {
            // Idempotence : la reservation est deja posee, on renvoie la meme reponse.
            logger.LogInformation(
                "Reservation {Reference} deja active : reponse idempotente",
                reference);

            return (await ToResponseAsync(existing, cancellationToken), true);
        }

        if (existing is { Status: ReservationStatus.Consommee })
        {
            throw new ConflictException(
                $"La reservation '{reference}' a deja ete consommee par une expedition : " +
                "elle ne peut plus etre reposee.",
                "RESERVATION_ALREADY_CONSUMED");
        }

        // Regroupement des lignes identiques (meme produit, meme entrepot demande).
        var demands = request.Lines
            .GroupBy(l => new { l.ProductId, l.WarehouseId })
            .Select(g => new ReservationLineRequest
            {
                ProductId = g.Key.ProductId,
                WarehouseId = g.Key.WarehouseId,
                Quantity = g.Sum(l => l.Quantity)
            })
            .ToList();

        // 1. Calcul complet de l'allocation, sans rien modifier.
        var allocations = new List<(StockItem Item, int Quantity)>();
        var shortages = new List<ShortageDetail>();

        foreach (var demand in demands)
        {
            var candidates = await LoadCandidatesAsync(demand, cancellationToken);
            var totalAvailable = candidates.Sum(c => c.QuantityAvailable);

            if (totalAvailable < demand.Quantity)
            {
                shortages.Add(new ShortageDetail
                {
                    ProductId = demand.ProductId,
                    Requested = demand.Quantity,
                    Available = totalAvailable
                });

                continue;
            }

            var remaining = demand.Quantity;

            // Les entrepots les mieux fournis sont servis en premier.
            foreach (var candidate in candidates.OrderByDescending(c => c.QuantityAvailable))
            {
                if (remaining == 0)
                {
                    break;
                }

                var take = Math.Min(remaining, candidate.QuantityAvailable);

                if (take <= 0)
                {
                    continue;
                }

                allocations.Add((candidate, take));
                remaining -= take;
            }
        }

        // 2. Si une seule ligne manque, rien n'est reserve : la saga saura compenser.
        if (shortages.Count > 0)
        {
            logger.LogWarning(
                "Reservation {Reference} refusee : {Count} produit(s) en stock insuffisant",
                reference,
                shortages.Count);

            throw new ConflictException(
                "Stock insuffisant pour " + string.Join(", ", shortages.Select(s =>
                    $"le produit {s.ProductId} ({s.Requested} demande(s), {s.Available} disponible(s))")) + ".",
                "INSUFFICIENT_STOCK",
                new Dictionary<string, object?> { ["shortages"] = shortages });
        }

        // 3. Application de l'allocation.
        var reservation = existing ?? new StockReservation
        {
            Reference = reference,
            CreatedBy = currentUser.UserName ?? "service-interne"
        };

        if (existing is not null)
        {
            // Reservation precedemment liberee : on la remet en service proprement.
            context.ReservationLines.RemoveRange(existing.Lines);
            existing.Lines.Clear();
            existing.Status = ReservationStatus.Active;
            existing.ResolvedAt = null;
            existing.CreatedAt = DateTimeOffset.UtcNow;
            existing.CreatedBy = currentUser.UserName ?? "service-interne";
        }
        else
        {
            context.Reservations.Add(reservation);
        }

        foreach (var (item, quantity) in allocations)
        {
            item.QuantityReserved += quantity;

            reservation.Lines.Add(new StockReservationLine
            {
                ReservationId = reservation.Id,
                ProductId = item.ProductId,
                WarehouseId = item.WarehouseId,
                Quantity = quantity
            });

            ledger.RecordMovement(item, MovementType.Reservation, quantity, reference, "Confirmation de commande");
        }

        await context.SaveChangesAsync(cancellationToken);

        // 4. Alertes de stock bas APRES enregistrement (le disponible a baisse).
        foreach (var (item, _) in allocations)
        {
            ledger.NotifyIfLowStock(item, reference);
        }

        logger.LogInformation(
            "Reservation {Reference} posee : {Lines} ligne(s), {Total} unite(s)",
            reference,
            reservation.Lines.Count,
            reservation.Lines.Sum(l => l.Quantity));

        return (await ToResponseAsync(reservation, cancellationToken), false);
    }

    /// <summary>
    /// Libere une reservation : le stock redevient disponible.
    /// C'est l'action de <b>compensation</b> de la saga, et aussi ce qui se passe
    /// quand une commande confirmee est annulee.
    /// Idempotent : liberer une reservation deja liberee renvoie simplement son etat.
    /// </summary>
    public async Task<ReservationResponse> ReleaseAsync(
        string reference,
        string? reason,
        CancellationToken cancellationToken)
    {
        var normalized = reference.Trim();

        var reservation = await context.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == normalized, cancellationToken)
            ?? throw new NotFoundException("La reservation", normalized);

        if (reservation.Status == ReservationStatus.Liberee)
        {
            logger.LogInformation("Reservation {Reference} deja liberee : reponse idempotente", normalized);

            return await ToResponseAsync(reservation, cancellationToken);
        }

        if (reservation.Status == ReservationStatus.Consommee)
        {
            throw new ConflictException(
                $"La reservation '{normalized}' a ete consommee par une expedition : elle ne peut plus etre liberee. " +
                "Passez par une entree de stock (retour marchandise) si necessaire.",
                "RESERVATION_ALREADY_CONSUMED");
        }

        var items = await LoadReservedItemsAsync(reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            var item = items[(line.ProductId, line.WarehouseId)];

            // Ne jamais descendre sous zero, meme si les donnees avaient derive.
            item.QuantityReserved = Math.Max(0, item.QuantityReserved - line.Quantity);

            ledger.RecordMovement(
                item,
                MovementType.LiberationReservation,
                line.Quantity,
                normalized,
                reason ?? "Liberation de reservation");
        }

        reservation.Status = ReservationStatus.Liberee;
        reservation.ResolvedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Reservation {Reference} liberee ({Total} unite(s) rendues disponibles) - motif : {Reason}",
            normalized,
            reservation.Lines.Sum(l => l.Quantity),
            reason ?? "non precise");

        return await ToResponseAsync(reservation, cancellationToken);
    }

    /// <summary>
    /// Consomme une reservation : le stock reserve devient une <b>sortie definitive</b>.
    /// Appele au passage de la commande a l'etat Expediee.
    /// Idempotent : consommer une reservation deja consommee renvoie son etat.
    /// </summary>
    public async Task<ReservationResponse> CommitAsync(
        string reference,
        string? reason,
        CancellationToken cancellationToken)
    {
        var normalized = reference.Trim();

        var reservation = await context.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == normalized, cancellationToken)
            ?? throw new NotFoundException("La reservation", normalized);

        if (reservation.Status == ReservationStatus.Consommee)
        {
            logger.LogInformation("Reservation {Reference} deja consommee : reponse idempotente", normalized);

            return await ToResponseAsync(reservation, cancellationToken);
        }

        if (reservation.Status == ReservationStatus.Liberee)
        {
            throw new ConflictException(
                $"La reservation '{normalized}' a ete liberee : elle ne peut pas etre consommee. " +
                "Reconfirmez la commande pour reposer une reservation.",
                "RESERVATION_RELEASED");
        }

        var items = await LoadReservedItemsAsync(reservation, cancellationToken);
        var affected = new List<StockItem>();

        foreach (var line in reservation.Lines)
        {
            var item = items[(line.ProductId, line.WarehouseId)];

            if (item.QuantityOnHand < line.Quantity)
            {
                // Cas theoriquement impossible (la contrainte base l'interdit), mais
                // un message explicite vaut mieux qu'une violation de contrainte.
                throw new ConflictException(
                    $"Incoherence de stock sur le produit {line.ProductId} : {line.Quantity} unite(s) reservee(s) " +
                    $"mais seulement {item.QuantityOnHand} en stock physique.",
                    "STOCK_INCONSISTENCY");
            }

            item.QuantityOnHand -= line.Quantity;
            item.QuantityReserved = Math.Max(0, item.QuantityReserved - line.Quantity);

            ledger.RecordMovement(
                item,
                MovementType.SortieSurReservation,
                line.Quantity,
                normalized,
                reason ?? "Expedition de commande");

            affected.Add(item);
        }

        reservation.Status = ReservationStatus.Consommee;
        reservation.ResolvedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        foreach (var item in affected)
        {
            ledger.NotifyIfLowStock(item, normalized);
        }

        logger.LogInformation(
            "Reservation {Reference} consommee : {Total} unite(s) sorties definitivement",
            normalized,
            reservation.Lines.Sum(l => l.Quantity));

        return await ToResponseAsync(reservation, cancellationToken);
    }

    /// <summary>Recupere une reservation par sa reference.</summary>
    public async Task<ReservationResponse> GetAsync(string reference, CancellationToken cancellationToken)
    {
        var normalized = reference.Trim();

        var reservation = await context.Reservations
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == normalized, cancellationToken)
            ?? throw new NotFoundException("La reservation", normalized);

        return await ToResponseAsync(reservation, cancellationToken);
    }

    /// <summary>Liste paginee des reservations, filtrable par etat.</summary>
    public async Task<PagedResult<ReservationResponse>> SearchAsync(
        PageQuery query,
        ReservationStatus? status,
        CancellationToken cancellationToken)
    {
        var source = context.Reservations
            .AsNoTracking()
            .Include(r => r.Lines)
            .AsQueryable();

        if (status is { } value)
        {
            source = source.Where(r => r.Status == value);
        }

        var page = await source
            .OrderByDescending(r => r.CreatedAt)
            .ToPagedResultAsync(query, r => r, cancellationToken);

        var codes = await LoadWarehouseCodesAsync(cancellationToken);

        return PagedResult<ReservationResponse>.Create(
            page.Items.Select(r => ToResponse(r, codes)).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    // ------------------------------- Helpers -------------------------------

    /// <summary>
    /// Lignes de stock candidates pour une demande : l'entrepot impose s'il est
    /// precise, sinon tous les entrepots actifs portant ce produit.
    /// </summary>
    private async Task<List<StockItem>> LoadCandidatesAsync(
        ReservationLineRequest demand,
        CancellationToken cancellationToken)
    {
        var query = context.StockItems
            .Include(s => s.Warehouse)
            .Where(s => s.ProductId == demand.ProductId);

        query = demand.WarehouseId is { } warehouseId
            ? query.Where(s => s.WarehouseId == warehouseId)
            : query.Where(s => s.Warehouse!.IsActive);

        var items = await query.ToListAsync(cancellationToken);

        // L'entrepot explicitement demande doit lui aussi etre actif.
        return items.Where(s => s.Warehouse is null || s.Warehouse.IsActive).ToList();
    }

    private async Task<Dictionary<(Guid ProductId, Guid WarehouseId), StockItem>> LoadReservedItemsAsync(
        StockReservation reservation,
        CancellationToken cancellationToken)
    {
        var productIds = reservation.Lines.Select(l => l.ProductId).Distinct().ToList();
        var warehouseIds = reservation.Lines.Select(l => l.WarehouseId).Distinct().ToList();

        var items = await context.StockItems
            .Include(s => s.Warehouse)
            .Where(s => productIds.Contains(s.ProductId) && warehouseIds.Contains(s.WarehouseId))
            .ToListAsync(cancellationToken);

        var map = items.ToDictionary(s => (s.ProductId, s.WarehouseId));

        var missing = reservation.Lines
            .Where(l => !map.ContainsKey((l.ProductId, l.WarehouseId)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new ConflictException(
                "La reservation reference des lignes de stock qui n'existent plus : " +
                string.Join(", ", missing.Select(l => $"produit {l.ProductId} / entrepot {l.WarehouseId}")) + ".",
                "STOCK_ITEM_MISSING");
        }

        return map;
    }

    private async Task<Dictionary<Guid, string>> LoadWarehouseCodesAsync(CancellationToken cancellationToken) =>
        await context.Warehouses
            .AsNoTracking()
            .ToDictionaryAsync(w => w.Id, w => w.Code, cancellationToken);

    private async Task<ReservationResponse> ToResponseAsync(
        StockReservation reservation,
        CancellationToken cancellationToken)
        => ToResponse(reservation, await LoadWarehouseCodesAsync(cancellationToken));

    private static ReservationResponse ToResponse(
        StockReservation reservation,
        IReadOnlyDictionary<Guid, string> warehouseCodes) => new()
    {
        Id = reservation.Id,
        Reference = reservation.Reference,
        Status = reservation.Status,
        CreatedBy = reservation.CreatedBy,
        CreatedAt = reservation.CreatedAt,
        ResolvedAt = reservation.ResolvedAt,
        Lines = reservation.Lines
            .OrderBy(l => l.ProductId)
            .Select(l => new ReservationLineResponse
            {
                ProductId = l.ProductId,
                WarehouseId = l.WarehouseId,
                WarehouseCode = warehouseCodes.TryGetValue(l.WarehouseId, out var code) ? code : string.Empty,
                Quantity = l.Quantity
            })
            .ToList()
    };
}

/// <summary>Chemin de code theoriquement inatteignable.</summary>
internal sealed class UnreachableException() : Exception("Chemin de code inatteignable.");
