using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using GestionCommandes.OrderService.Services;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Controllers;

/// <summary>
/// Commandes : saisie du brouillon, puis les quatre transitions du cycle de vie.
///
/// Regle d'acces : le back-office voit et pilote tout ; un compte de role Client
/// ne voit et ne touche que SES commandes.
/// </summary>
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(
    OrderDbContext context,
    OrderWorkflow workflow,
    ICurrentUser currentUser)
    : ControllerBase
{
    /// <summary>Decrit le cycle de vie des commandes : etats et transitions possibles.</summary>
    [HttpGet("workflow")]
    [AllowAnonymous]
    public ActionResult<object> Workflow() => Ok(new
    {
        etats = Enum.GetValues<OrderStatus>().Select(status => new
        {
            etat = status.ToString(),
            transitions = OrderStateMachine.NextStates(status).Select(next => next.ToString()),
            final = OrderStateMachine.IsFinal(status),
            lignesModifiables = OrderStateMachine.AllowsLineEdition(status),
            stockImmobilise = OrderStateMachine.HoldsStock(status)
        })
    });

    /// <summary>Liste paginee des commandes.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderResponse>>> List(
        [FromQuery] OrderQuery query,
        CancellationToken cancellationToken)
    {
        var orders = context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .AsQueryable();

        // Un client ne voit que ses propres commandes, quel que soit le filtre demande.
        if (!currentUser.IsBackOffice)
        {
            var customerId = RequireCustomerId();
            orders = orders.Where(o => o.CustomerId == customerId);
        }
        else if (query.CustomerId is { } filtered)
        {
            orders = orders.Where(o => o.CustomerId == filtered);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            orders = orders.Where(o => EF.Functions.Like(o.Number, $"%{search}%"));
        }

        if (query.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }

        var page = await orders
            .OrderByDescending(o => o.CreatedAt)
            .ToPageAsync(query, OrderMapper.ToResponse, cancellationToken);

        return Ok(page);
    }

    /// <summary>Renvoie une commande, lignes et historique compris.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var order = await workflow.LoadAsync(id, cancellationToken);

        EnsureCanAccess(order);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>Cree une commande en brouillon.</summary>
    /// <param name="request">Client, adresse et lignes initiales.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    public async Task<ActionResult<OrderResponse>> Create(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Un client commande toujours pour lui-meme : on ignore le customerId recu.
        var customerId = currentUser.IsBackOffice
            ? request.CustomerId ?? throw new BadRequestException("customerId est obligatoire.")
            : RequireCustomerId();

        var customer = await context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
                       ?? throw new NotFoundException("Le client", customerId);

        if (!customer.IsActive)
        {
            throw new ConflictException(
                $"Le client {customer.Code} est desactive : il ne peut plus passer de commande.");
        }

        var order = new Order
        {
            Number = await workflow.NextNumberAsync(cancellationToken),
            CustomerId = customer.Id,
            ShippingAddress = request.ShippingAddress ?? customer.Address,
            ShippingCity = request.ShippingCity ?? customer.City,
            Notes = request.Notes,
            CreatedBy = currentUser.UserName
        };

        foreach (var line in MergeLines(request.Lines))
        {
            order.Lines.Add(new OrderLine { ProductId = line.Key, Quantity = line.Value });
        }

        order.AppendHistory(null, OrderStatus.Brouillon, currentUser.UserName, "Creation du brouillon");

        context.Orders.Add(order);
        await context.SaveChangesAsync(cancellationToken);

        var created = await workflow.LoadAsync(order.Id, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = order.Id }, OrderMapper.ToResponse(created));
    }

    /// <summary>Met a jour les informations de livraison d'un brouillon.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrderResponse>> Update(
        Guid id,
        UpdateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadEditableAsync(id, cancellationToken);

        order.ShippingAddress = request.ShippingAddress ?? order.ShippingAddress;
        order.ShippingCity = request.ShippingCity ?? order.ShippingCity;
        order.Notes = request.Notes;

        await context.SaveChangesAsync(cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>Ajoute une ligne a un brouillon, ou cumule la quantite si le produit y est deja.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="request">Produit et quantite.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/lines")]
    public async Task<ActionResult<OrderResponse>> AddLine(
        Guid id,
        OrderLineRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadEditableAsync(id, cancellationToken);

        var existing = order.Lines.FirstOrDefault(line => line.ProductId == request.ProductId);

        if (existing is null)
        {
            order.Lines.Add(new OrderLine { OrderId = order.Id, ProductId = request.ProductId, Quantity = request.Quantity });
        }
        else
        {
            existing.Quantity += request.Quantity;
        }

        await context.SaveChangesAsync(cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>Modifie la quantite d'une ligne d'un brouillon.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="lineId">Identifiant de la ligne.</param>
    /// <param name="request">Nouvelle quantite.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}/lines/{lineId:long}")]
    public async Task<ActionResult<OrderResponse>> UpdateLine(
        Guid id,
        long lineId,
        UpdateLineRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadEditableAsync(id, cancellationToken);

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new NotFoundException("La ligne de commande", lineId);

        line.Quantity = request.Quantity;

        await context.SaveChangesAsync(cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>Supprime une ligne d'un brouillon.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="lineId">Identifiant de la ligne.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpDelete("{id:guid}/lines/{lineId:long}")]
    public async Task<ActionResult<OrderResponse>> RemoveLine(
        Guid id,
        long lineId,
        CancellationToken cancellationToken)
    {
        var order = await LoadEditableAsync(id, cancellationToken);

        var line = order.Lines.FirstOrDefault(l => l.Id == lineId)
                   ?? throw new NotFoundException("La ligne de commande", lineId);

        order.Lines.Remove(line);

        await context.SaveChangesAsync(cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>
    /// Confirme la commande : le stock est reserve et les prix sont figes.
    /// </summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="200">Commande confirmee.</response>
    /// <response code="409">Stock insuffisant, produit inactif ou transition interdite.</response>
    /// <response code="503">catalog-service injoignable.</response>
    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<OrderResponse>> Confirm(Guid id, CancellationToken cancellationToken)
    {
        EnsureCanAccess(await workflow.LoadAsync(id, cancellationToken));

        var order = await workflow.ConfirmAsync(id, cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>Passe la commande en preparation.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/prepare")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<OrderResponse>> Prepare(Guid id, CancellationToken cancellationToken) =>
        Ok(OrderMapper.ToResponse(await workflow.StartPreparationAsync(id, cancellationToken)));

    /// <summary>Expedie la commande : sortie de stock et creation du bordereau.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<OrderResponse>> Ship(Guid id, CancellationToken cancellationToken) =>
        Ok(OrderMapper.ToResponse(await workflow.ShipAsync(id, cancellationToken)));

    /// <summary>Annule la commande et libere le stock si besoin.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="request">Motif d'annulation.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<OrderResponse>> Cancel(
        Guid id,
        CancelOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCanAccess(await workflow.LoadAsync(id, cancellationToken));

        var order = await workflow.CancelAsync(id, request.Reason, cancellationToken);

        return Ok(OrderMapper.ToResponse(order));
    }

    /// <summary>
    /// Marque la commande comme livree. Appel interne : c'est delivery-service
    /// qui previent quand le livreur a confirme la remise.
    /// </summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/delivered")]
    [Authorize(Roles = Roles.Service)]
    public async Task<ActionResult<OrderResponse>> MarkDelivered(Guid id, CancellationToken cancellationToken) =>
        Ok(OrderMapper.ToResponse(await workflow.MarkDeliveredAsync(id, cancellationToken)));

    // =====================================================================
    //  Outils internes
    // =====================================================================

    /// <summary>Charge une commande modifiable, apres controle d'acces et d'etat.</summary>
    private async Task<Order> LoadEditableAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await workflow.LoadAsync(id, cancellationToken);

        EnsureCanAccess(order);

        if (!OrderStateMachine.AllowsLineEdition(order.Status))
        {
            throw new ConflictException(
                $"La commande {order.Number} est « {order.Status} » : son contenu n'est plus modifiable. " +
                "Seul un brouillon peut etre modifie.");
        }

        return order;
    }

    /// <summary>Verifie qu'un client ne consulte pas la commande d'un autre.</summary>
    private void EnsureCanAccess(Order order)
    {
        if (currentUser.IsBackOffice)
        {
            return;
        }

        if (RequireCustomerId() != order.CustomerId)
        {
            throw new ForbiddenException($"La commande {order.Number} n'appartient pas a votre compte.");
        }
    }

    private Guid RequireCustomerId() =>
        currentUser.CustomerId
        ?? throw new ForbiddenException(
            "Votre compte n'est rattache a aucun client : demandez a un administrateur de renseigner customerId.");

    /// <summary>Regroupe les lignes d'une meme reference et additionne les quantites.</summary>
    private static Dictionary<Guid, int> MergeLines(List<OrderLineRequest> lines) =>
        lines
            .GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
}

/// <summary>Projections des commandes vers les formats exposes par l'API.</summary>
public static class OrderMapper
{
    /// <summary>Convertit une commande.</summary>
    /// <param name="order">Commande, lignes et historique charges si disponibles.</param>
    public static OrderResponse ToResponse(Order order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        CustomerId = order.CustomerId,
        CustomerName = order.Customer?.Name,
        Status = order.Status,
        AllowedTransitions = OrderStateMachine.NextStates(order.Status).Select(status => status.ToString()).ToList(),
        ShippingAddress = order.ShippingAddress,
        ShippingCity = order.ShippingCity,
        Notes = order.Notes,
        SubTotal = order.SubTotal,
        VatRate = order.VatRate,
        VatAmount = order.VatAmount,
        Total = order.Total,
        Currency = order.Currency,
        LastError = order.LastError,
        DeliveryId = order.DeliveryId,
        CreatedAt = order.CreatedAt,
        ConfirmedAt = order.ConfirmedAt,
        ShippedAt = order.ShippedAt,
        DeliveredAt = order.DeliveredAt,
        CancelledAt = order.CancelledAt,
        CancellationReason = order.CancellationReason,
        Lines = order.Lines
            .OrderBy(line => line.Id)
            .Select(line => new OrderLineResponse
            {
                Id = line.Id,
                ProductId = line.ProductId,
                Sku = line.Sku,
                ProductName = line.ProductName,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                LineTotal = line.LineTotal
            })
            .ToList(),
        History = order.History
            .OrderBy(entry => entry.ChangedAt)
            .Select(entry => new OrderHistoryResponse
            {
                FromStatus = entry.FromStatus,
                ToStatus = entry.ToStatus,
                Reason = entry.Reason,
                ChangedBy = entry.ChangedBy,
                ChangedAt = entry.ChangedAt
            })
            .ToList()
    };
}
