using System.Text.Json;
using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Services;

/// <summary>
/// Cycle de vie « statique » de la commande : creation, consultation, et edition
/// des lignes tant que la commande est en brouillon. Les transitions d'etat qui
/// impliquent d'autres services sont dans <see cref="OrderSaga"/>.
/// </summary>
public sealed class OrderManager(
    OrderDbContext context,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    ILogger<OrderManager> logger)
{
    /// <summary>Operation d'idempotence pour la creation de commande.</summary>
    public const string CreateOperation = "orders.create";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Liste paginee des commandes, filtree selon le role de l'appelant.</summary>
    public async Task<PagedResult<OrderResponse>> SearchAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new BadRequestException("from ne peut pas etre posterieur a to.", "INVALID_DATE_RANGE");
        }

        var source = context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .AsQueryable();

        // Un client ne voit que SES commandes, meme s'il demande celles d'un autre.
        var scopedCustomerId = ResolveCustomerScope(query.CustomerId);

        if (scopedCustomerId is { } customerId)
        {
            source = source.Where(o => o.CustomerId == customerId);
        }

        if (query.Status is { } status)
        {
            source = source.Where(o => o.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Number))
        {
            var number = query.Number.Trim();
            source = source.Where(o => EF.Functions.Like(o.Number, $"%{number}%"));
        }

        if (query.From is { } fromDate)
        {
            source = source.Where(o => o.CreatedAt >= fromDate);
        }

        if (query.To is { } toDate)
        {
            source = source.Where(o => o.CreatedAt <= toDate);
        }

        source = query.Sort?.ToLowerInvariant() switch
        {
            "createdat" => source.OrderBy(o => o.CreatedAt),
            "number" => source.OrderBy(o => o.Number),
            "number_desc" => source.OrderByDescending(o => o.Number),
            "total" => source.OrderBy(o => o.Total),
            "total_desc" => source.OrderByDescending(o => o.Total),
            _ => source.OrderByDescending(o => o.CreatedAt)
        };

        return await source.ToPagedResultAsync(query, OrderMapper.ToResponse, cancellationToken);
    }

    /// <summary>Recupere une commande par identifiant.</summary>
    public async Task<OrderResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: false, cancellationToken);

        EnsureCanAccess(order);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Recupere une commande par numero.</summary>
    public async Task<OrderResponse> GetByNumberAsync(string number, CancellationToken cancellationToken)
    {
        var normalized = number.Trim().ToUpperInvariant();

        var order = await context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Number == normalized, cancellationToken)
            ?? throw new NotFoundException("La commande", number);

        EnsureCanAccess(order);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Historique des changements d'etat d'une commande.</summary>
    public async Task<IReadOnlyList<OrderHistoryResponse>> GetHistoryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: false, cancellationToken);

        EnsureCanAccess(order);

        return await context.OrderHistory
            .AsNoTracking()
            .Where(h => h.OrderId == id)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new OrderHistoryResponse
            {
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                Reason = h.Reason,
                ChangedBy = h.ChangedBy,
                ChangedAt = h.ChangedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Cree une commande en Brouillon. Aucun prix n'est fige et aucun stock n'est
    /// engage a ce stade : c'est la confirmation qui declenche la saga.
    ///
    /// Si un en-tete <c>Idempotency-Key</c> est fourni, un rejeu de la requete
    /// renvoie la commande deja creee au lieu d'en creer une seconde.
    /// </summary>
    public async Task<(OrderResponse Order, bool Replayed)> CreateAsync(
        CreateOrderRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var previous = await idempotency.FindAsync(CreateOperation, idempotencyKey, cancellationToken);

            if (previous is not null)
            {
                logger.LogInformation(
                    "Creation de commande rejouee avec la cle d'idempotence {Key} : commande {ResourceId}",
                    idempotencyKey,
                    previous.ResourceId);

                var replayed = JsonSerializer.Deserialize<OrderResponse>(previous.ResponseBody, JsonOptions)
                               ?? throw new AppException(
                                   System.Net.HttpStatusCode.InternalServerError,
                                   "Journal d'idempotence corrompu",
                                   "La reponse enregistree pour cette cle d'idempotence est illisible.",
                                   "IDEMPOTENCY_CORRUPTED");

                return (replayed, true);
            }
        }

        var customerId = ResolveCustomerForCreation(request.CustomerId);

        var customer = await context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new BadRequestException(
                           $"Le client '{customerId}' est inconnu.",
                           "UNKNOWN_CUSTOMER");

        if (!customer.IsActive)
        {
            throw new ConflictException(
                $"Le client '{customer.Code}' est desactive : aucune nouvelle commande ne peut etre saisie.",
                "CUSTOMER_INACTIVE");
        }

        var duplicates = request.Lines
            .GroupBy(l => l.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            throw new BadRequestException(
                $"Les produits suivants apparaissent plusieurs fois : {string.Join(", ", duplicates)}. " +
                "Regroupez les quantites sur une seule ligne par produit.",
                "DUPLICATE_PRODUCT_LINE");
        }

        var order = new Order
        {
            Number = await NextOrderNumberAsync(cancellationToken),
            CustomerId = customer.Id,
            Status = OrderStatus.Brouillon,
            ShippingAddress = request.ShippingAddress?.Trim() ?? customer.Address,
            ShippingCity = request.ShippingCity?.Trim() ?? customer.City,
            Notes = request.Notes?.Trim(),
            Currency = DemoData.Currency,
            CreatedBy = currentUser.UserName ?? "systeme",
            Lines = request.Lines.Select(l => new OrderLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitPrice = 0m,
                LineTotal = 0m
            }).ToList()
        };

        order.RecomputeTotals(DemoData.VatRate);
        order.AppendHistory(null, OrderStatus.Brouillon, order.CreatedBy, "Creation de la commande");

        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        order.Customer = customer;
        var response = OrderMapper.ToResponse(order);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await idempotency.SaveAsync(
                CreateOperation,
                idempotencyKey,
                StatusCodes.Status201Created,
                JsonSerializer.Serialize(response, JsonOptions),
                order.Id.ToString(),
                cancellationToken);
        }

        logger.LogInformation(
            "Commande {Number} creee en brouillon pour le client {Customer} ({Lines} ligne(s))",
            order.Number,
            customer.Code,
            order.Lines.Count);

        return (response, false);
    }

    /// <summary>Met a jour les informations d'expedition d'un brouillon.</summary>
    public async Task<OrderResponse> UpdateAsync(
        Guid id,
        UpdateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(order);
        EnsureDraft(order, "modifier les informations d'expedition");

        order.ShippingAddress = request.ShippingAddress?.Trim();
        order.ShippingCity = request.ShippingCity?.Trim();
        order.Notes = request.Notes?.Trim();

        await context.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Ajoute une ligne a un brouillon (ou cumule la quantite si le produit y figure deja).</summary>
    public async Task<OrderResponse> AddLineAsync(
        Guid id,
        OrderLineRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(order);
        EnsureDraft(order, "ajouter une ligne");

        var existing = order.Lines.FirstOrDefault(l => l.ProductId == request.ProductId);

        if (existing is not null)
        {
            existing.Quantity += request.Quantity;
        }
        else
        {
            if (order.Lines.Count >= 200)
            {
                throw new BadRequestException("Une commande ne peut pas depasser 200 lignes.", "TOO_MANY_LINES");
            }

            order.Lines.Add(new OrderLine
            {
                OrderId = order.Id,
                ProductId = request.ProductId,
                Quantity = request.Quantity,
                UnitPrice = 0m,
                LineTotal = 0m
            });
        }

        order.RecomputeTotals(DemoData.VatRate);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Ligne {ProductId} x{Quantity} ajoutee a la commande {Number}",
            request.ProductId,
            request.Quantity,
            order.Number);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Modifie la quantite d'une ligne de brouillon.</summary>
    public async Task<OrderResponse> UpdateLineAsync(
        Guid id,
        long lineId,
        UpdateOrderLineRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(order);
        EnsureDraft(order, "modifier une ligne");

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new NotFoundException("La ligne de commande", lineId);

        line.Quantity = request.Quantity;
        line.LineTotal = Math.Round(line.UnitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);

        order.RecomputeTotals(DemoData.VatRate);
        await context.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Supprime une ligne de brouillon.</summary>
    public async Task<OrderResponse> RemoveLineAsync(Guid id, long lineId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(order);
        EnsureDraft(order, "supprimer une ligne");

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new NotFoundException("La ligne de commande", lineId);

        order.Lines.Remove(line);
        context.OrderLines.Remove(line);

        order.RecomputeTotals(DemoData.VatRate);
        await context.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToResponse(order);
    }

    /// <summary>Supprime un brouillon. Une commande engagee n'est jamais supprimee, elle est annulee.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(order);

        if (order.Status != OrderStatus.Brouillon)
        {
            throw new ConflictException(
                $"La commande {order.Number} est dans l'etat « {order.Status} » : elle ne peut pas etre supprimee. " +
                "Utilisez l'annulation pour conserver l'historique.",
                "ORDER_NOT_DELETABLE",
                new Dictionary<string, object?> { ["currentStatus"] = order.Status.ToString() });
        }

        context.Orders.Remove(order);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Brouillon de commande {Number} supprime", order.Number);
    }

    // ------------------------------- Helpers -------------------------------

    /// <summary>Charge une commande avec son client et ses lignes.</summary>
    public async Task<Order> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(o => o.Id == id, cancellationToken)
               ?? throw new NotFoundException("La commande", id);
    }

    /// <summary>
    /// Un compte de role Client ne peut consulter ou modifier que ses propres
    /// commandes : la restriction vient du claim <c>customer_id</c> du jeton,
    /// jamais d'un parametre de requete.
    /// </summary>
    public void EnsureCanAccess(Order order)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return;
        }

        if (currentUser.IsInRole(Roles.Client) && currentUser.CustomerId == order.CustomerId)
        {
            return;
        }

        throw new ForbiddenException(
            $"Votre compte n'a pas acces a la commande {order.Number}.");
    }

    private static void EnsureDraft(Order order, string action)
    {
        if (OrderStateMachine.AllowsLineEdition(order.Status))
        {
            return;
        }

        throw new ConflictException(
            $"Impossible de {action} : la commande {order.Number} est dans l'etat « {order.Status} ». " +
            "Seul un brouillon est modifiable.",
            "ORDER_NOT_EDITABLE",
            new Dictionary<string, object?> { ["currentStatus"] = order.Status.ToString() });
    }

    private Guid? ResolveCustomerScope(Guid? requested)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return requested;
        }

        // Role Client : on force le perimetre sur son propre identifiant.
        return currentUser.CustomerId
               ?? throw new ForbiddenException(
                   "Votre compte n'est rattache a aucun client : aucune commande ne peut etre listee. " +
                   "Un administrateur doit renseigner customerId sur votre compte.");
    }

    private Guid ResolveCustomerForCreation(Guid? requested)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return requested
                   ?? throw new BadRequestException(
                       "customerId est obligatoire : un compte back-office doit designer le client de la commande.",
                       "CUSTOMER_ID_REQUIRED");
        }

        var own = currentUser.CustomerId
                  ?? throw new ForbiddenException(
                      "Votre compte n'est rattache a aucun client : aucune commande ne peut etre creee.");

        if (requested is { } asked && asked != own)
        {
            throw new ForbiddenException("Vous ne pouvez creer de commande que pour votre propre compte client.");
        }

        return own;
    }

    /// <summary>
    /// Attribue le prochain numero de commande de l'annee en cours
    /// (format <c>CMD-2026-00001</c>), via un compteur persiste.
    /// </summary>
    private async Task<string> NextOrderNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;

        var sequence = await context.OrderSequences.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new OrderSequence { Year = year, LastValue = 0 };
            context.OrderSequences.Add(sequence);
        }

        sequence.LastValue++;

        return $"CMD-{year}-{sequence.LastValue:D5}";
    }
}
