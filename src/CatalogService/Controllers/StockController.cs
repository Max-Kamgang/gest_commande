using GestionCommandes.CatalogService.Data;
using GestionCommandes.CatalogService.Domain;
using GestionCommandes.CatalogService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Controllers;

/// <summary>
/// Stock : niveaux, mouvements, entrees, ajustements et reservations.
///
/// Les reservations sont le point de contact avec order-service. Elles sont
/// toutes REJOUABLES : rappeler le meme endpoint avec la meme reference de
/// commande ne double jamais l'effet. C'est ce qui permet a order-service de
/// reessayer sans risque quand le reseau Docker a hesite.
/// </summary>
[ApiController]
[Route("api/stock")]
[Authorize]
public sealed class StockController(
    CatalogDbContext context,
    ICurrentUser currentUser,
    ILogger<StockController> logger)
    : ControllerBase
{
    /// <summary>Niveaux de stock, pagines.</summary>
    /// <param name="query">Filtres, tri et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> Levels(
        [FromQuery] ProductQuery query,
        CancellationToken cancellationToken)
    {
        var products = context.Products.Include(p => p.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            products = products.Where(p =>
                EF.Functions.Like(p.Sku, $"%{search}%") || EF.Functions.Like(p.Name, $"%{search}%"));
        }

        if (query.LowStockOnly == true)
        {
            products = products.Where(p => p.QuantityOnHand - p.QuantityReserved <= p.LowStockThreshold);
        }

        products = query.Sort == "available_desc"
            ? products.OrderByDescending(p => p.QuantityOnHand - p.QuantityReserved)
            : products.OrderBy(p => p.QuantityOnHand - p.QuantityReserved);

        return Ok(await products.ToPageAsync(query, produit => CatalogMapper.ToResponse(produit), cancellationToken));
    }

    /// <summary>Produits dont le disponible est au niveau du seuil ou en dessous.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("low-stock")]
    public async Task<ActionResult<IEnumerable<ProductResponse>>> LowStock(CancellationToken cancellationToken)
    {
        var products = await context.Products
            .Include(p => p.Category)
            .Where(p => p.QuantityOnHand - p.QuantityReserved <= p.LowStockThreshold)
            .OrderBy(p => p.QuantityOnHand - p.QuantityReserved)
            .ToListAsync(cancellationToken);

        return Ok(products.Select(CatalogMapper.ToResponse));
    }

    /// <summary>Historique pagine des mouvements de stock.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("movements")]
    public async Task<ActionResult<PagedResult<MovementResponse>>> Movements(
        [FromQuery] MovementQuery query,
        CancellationToken cancellationToken)
    {
        var movements = context.StockMovements.Include(m => m.Product).AsQueryable();

        if (query.ProductId is { } productId)
        {
            movements = movements.Where(m => m.ProductId == productId);
        }

        if (query.Type is { } type)
        {
            movements = movements.Where(m => m.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Reference))
        {
            var reference = query.Reference.Trim();
            movements = movements.Where(m => m.Reference == reference);
        }

        var page = await movements
            .OrderByDescending(m => m.Id)
            .ToPageAsync(query, mouvement => CatalogMapper.ToResponse(mouvement), cancellationToken);

        return Ok(page);
    }

    /// <summary>Enregistre une entree en stock.</summary>
    /// <param name="request">Produit, quantite et motif.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("entries")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> AddEntry(
        StockEntryRequest request,
        CancellationToken cancellationToken)
    {
        var product = await LoadProductAsync(request.ProductId, cancellationToken);

        product.QuantityOnHand += request.Quantity;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        AddMovement(product, MovementType.Entree, request.Quantity, reason: request.Reason);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Entree de {Quantite} sur {Sku} : stock physique {Stock}",
            request.Quantity, product.Sku, product.QuantityOnHand);

        return Ok(CatalogMapper.ToResponse(product));
    }

    /// <summary>Corrige la quantite physique apres un inventaire.</summary>
    /// <param name="request">Produit, quantite constatee et motif.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("adjustments")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> Adjust(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var product = await LoadProductAsync(request.ProductId, cancellationToken);

        // On ne descend pas sous ce qui est deja promis a des commandes confirmees :
        // ce serait creer une survente par un simple ajustement.
        if (request.QuantityOnHand < product.QuantityReserved)
        {
            throw new ConflictException(
                $"Le produit {product.Sku} a {product.QuantityReserved} unite(s) reservee(s) par des commandes " +
                $"confirmees : la quantite physique ne peut pas descendre a {request.QuantityOnHand}.");
        }

        var delta = request.QuantityOnHand - product.QuantityOnHand;

        product.QuantityOnHand = request.QuantityOnHand;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        AddMovement(product, MovementType.Ajustement, Math.Abs(delta), reason: request.Reason);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ajustement de {Sku} a {Stock} (ecart {Delta}) : {Motif}",
            product.Sku, product.QuantityOnHand, delta, request.Reason);

        return Ok(CatalogMapper.ToResponse(product));
    }

    // =====================================================================
    //  Reservations : appelees par order-service
    // =====================================================================

    /// <summary>
    /// Reserve le stock d'une commande et renvoie les prix catalogue a figer.
    ///
    /// Un seul appel couvre les deux besoins de la confirmation : verifier et
    /// immobiliser le stock, et donner les prix du moment. Rappeler cet endpoint
    /// avec la meme reference renvoie la reservation existante sans rien doubler.
    /// </summary>
    /// <param name="request">Reference de commande et lignes a reserver.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="200">Reservation posee, ou deja posee.</response>
    /// <response code="409">Stock insuffisant, produit inactif, ou reservation deja soldee.</response>
    [HttpPost("reservations")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ReservationResponse>> Reserve(
        ReserveStockRequest request,
        CancellationToken cancellationToken)
    {
        var reference = request.Reference.Trim();

        var existing = await context.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == reference, cancellationToken);

        if (existing is not null)
        {
            // Rejeu : la reservation est deja posee, on renvoie la meme reponse.
            if (existing.Status == ReservationStatus.Active)
            {
                logger.LogInformation("Reservation {Reference} deja active : rejeu sans effet.", reference);

                return Ok(await DescribeAsync(existing, cancellationToken));
            }

            throw new ConflictException(
                $"La reservation {reference} est deja {existing.Status.ToString().ToLowerInvariant()} : " +
                "elle ne peut plus etre reposee.");
        }

        // Les lignes sont regroupees par produit : deux lignes du meme produit
        // dans une commande ne doivent pas poser deux reservations concurrentes.
        var demands = request.Lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

        var requestedIds = demands.Keys.ToList();

        var products = await context.Products
            .Where(p => requestedIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var missing = requestedIds.Except(products.Select(p => p.Id)).ToList();

        if (missing.Count > 0)
        {
            throw new BadRequestException(
                $"Produit(s) inconnu(s) du catalogue : {string.Join(", ", missing)}.");
        }

        var inactive = products.Where(p => !p.IsActive).Select(p => p.Sku).ToList();

        if (inactive.Count > 0)
        {
            throw new ConflictException(
                $"Produit(s) desactive(s), non commandable(s) : {string.Join(", ", inactive)}.");
        }

        var insufficient = products
            .Where(p => p.QuantityAvailable < demands[p.Id])
            .Select(p => $"{p.Sku} (demande {demands[p.Id]}, disponible {p.QuantityAvailable})")
            .ToList();

        if (insufficient.Count > 0)
        {
            throw new ConflictException($"Stock insuffisant pour : {string.Join(" ; ", insufficient)}.");
        }

        var reservation = new StockReservation
        {
            Reference = reference,
            CreatedBy = currentUser.UserName
        };

        foreach (var product in products)
        {
            var quantity = demands[product.Id];

            product.QuantityReserved += quantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;

            AddMovement(product, MovementType.Reservation, quantity, reference: reference);

            reservation.Lines.Add(new StockReservationLine
            {
                ProductId = product.Id,
                Quantity = quantity,
                // Le prix est copie ici : c'est la valeur que order-service figera.
                UnitPrice = product.UnitPrice
            });
        }

        context.Reservations.Add(reservation);

        await SaveWithConcurrencyGuardAsync(reference, cancellationToken);

        logger.LogInformation(
            "Reservation {Reference} posee sur {Lignes} produit(s).", reference, reservation.Lines.Count);

        return Ok(await DescribeAsync(reservation, cancellationToken));
    }

    /// <summary>Renvoie une reservation.</summary>
    /// <param name="reference">Numero de commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("reservations/{reference}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ReservationResponse>> GetReservation(
        string reference,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadReservationAsync(reference, cancellationToken);

        return Ok(await DescribeAsync(reservation, cancellationToken));
    }

    /// <summary>
    /// Libere une reservation : le stock immobilise redevient disponible.
    /// Appele quand une commande est annulee, ou quand sa confirmation echoue.
    /// </summary>
    /// <param name="reference">Numero de commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("reservations/{reference}/release")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ReservationResponse>> Release(
        string reference,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadReservationAsync(reference, cancellationToken);

        // Rejeu : deja liberee, il n'y a rien a refaire.
        if (reservation.Status == ReservationStatus.Liberee)
        {
            return Ok(await DescribeAsync(reservation, cancellationToken));
        }

        if (reservation.Status == ReservationStatus.Consommee)
        {
            throw new ConflictException(
                $"La reservation {reservation.Reference} a deja ete consommee par une expedition : " +
                "le stock est sorti et ne peut pas etre rendu automatiquement.");
        }

        var products = await LoadReservedProductsAsync(reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            var product = products[line.ProductId];

            product.QuantityReserved -= line.Quantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;

            AddMovement(product, MovementType.Liberation, line.Quantity, reference: reservation.Reference);
        }

        reservation.Status = ReservationStatus.Liberee;
        reservation.ResolvedAt = DateTimeOffset.UtcNow;

        await SaveWithConcurrencyGuardAsync(reservation.Reference, cancellationToken);

        logger.LogInformation("Reservation {Reference} liberee.", reservation.Reference);

        return Ok(await DescribeAsync(reservation, cancellationToken));
    }

    /// <summary>
    /// Transforme une reservation en sortie definitive : la marchandise quitte
    /// le stock. Appele a l'expedition de la commande.
    /// </summary>
    /// <param name="reference">Numero de commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("reservations/{reference}/ship")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ReservationResponse>> Ship(
        string reference,
        CancellationToken cancellationToken)
    {
        var reservation = await LoadReservationAsync(reference, cancellationToken);

        // Rejeu : la sortie a deja ete faite.
        if (reservation.Status == ReservationStatus.Consommee)
        {
            return Ok(await DescribeAsync(reservation, cancellationToken));
        }

        if (reservation.Status == ReservationStatus.Liberee)
        {
            throw new ConflictException(
                $"La reservation {reservation.Reference} a ete liberee : le stock n'est plus immobilise, " +
                "la commande doit etre reconfirmee avant d'etre expediee.");
        }

        var products = await LoadReservedProductsAsync(reservation, cancellationToken);

        foreach (var line in reservation.Lines)
        {
            var product = products[line.ProductId];

            product.QuantityOnHand -= line.Quantity;
            product.QuantityReserved -= line.Quantity;
            product.UpdatedAt = DateTimeOffset.UtcNow;

            AddMovement(product, MovementType.Sortie, line.Quantity, reference: reservation.Reference);
        }

        reservation.Status = ReservationStatus.Consommee;
        reservation.ResolvedAt = DateTimeOffset.UtcNow;

        await SaveWithConcurrencyGuardAsync(reservation.Reference, cancellationToken);

        logger.LogInformation("Reservation {Reference} consommee : stock sorti.", reservation.Reference);

        return Ok(await DescribeAsync(reservation, cancellationToken));
    }

    // =====================================================================
    //  Outils internes
    // =====================================================================

    private async Task<Product> LoadProductAsync(Guid productId, CancellationToken cancellationToken) =>
        await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
        ?? throw new NotFoundException("Le produit", productId);

    private async Task<StockReservation> LoadReservationAsync(
        string reference,
        CancellationToken cancellationToken) =>
        await context.Reservations
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Reference == reference.Trim(), cancellationToken)
        ?? throw new NotFoundException("La reservation", reference);

    private async Task<Dictionary<Guid, Product>> LoadReservedProductsAsync(
        StockReservation reservation,
        CancellationToken cancellationToken)
    {
        var ids = reservation.Lines.Select(line => line.ProductId).ToList();

        var products = await context.Products
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var missing = ids.Except(products.Keys).ToList();

        if (missing.Count > 0)
        {
            throw new ConflictException(
                $"La reservation {reservation.Reference} porte sur des produits supprimes du catalogue : " +
                $"{string.Join(", ", missing)}.");
        }

        return products;
    }

    /// <summary>Ajoute la trace d'un mouvement, apres mise a jour des quantites.</summary>
    private void AddMovement(
        Product product,
        MovementType type,
        int quantity,
        string? reference = null,
        string? reason = null)
        => context.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Type = type,
            Quantity = quantity,
            QuantityOnHandAfter = product.QuantityOnHand,
            QuantityReservedAfter = product.QuantityReserved,
            Reference = reference,
            Reason = reason,
            PerformedBy = currentUser.UserName
        });

    /// <summary>
    /// Enregistre en traduisant un conflit de concurrence en 409 explicite :
    /// deux commandes ont touche le meme produit en meme temps, l'appelant doit
    /// simplement reessayer.
    /// </summary>
    private async Task SaveWithConcurrencyGuardAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                $"Le stock d'un produit de {reference} a ete modifie en meme temps par une autre operation. " +
                "Aucune modification n'a ete enregistree : reessayez.");
        }
    }

    /// <summary>Construit la reponse d'une reservation, avec les libelles produits.</summary>
    private async Task<ReservationResponse> DescribeAsync(
        StockReservation reservation,
        CancellationToken cancellationToken)
    {
        var ids = reservation.Lines.Select(line => line.ProductId).ToList();

        var labels = await context.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.Name })
            .ToListAsync(cancellationToken);

        return CatalogMapper.ToResponse(
            reservation,
            labels.ToDictionary(label => label.Id, label => (label.Sku, label.Name)));
    }
}
