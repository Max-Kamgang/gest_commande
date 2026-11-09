using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.DeliveryService.Services;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Controllers;

/// <summary>
/// Livraisons : bordereaux crees par order-service, puis suivis jusqu'a la remise.
///
/// Acces : le back-office voit tout et affecte les livreurs ; un compte de role
/// Livreur ne voit et ne met a jour que SES livraisons.
/// </summary>
[ApiController]
[Route("api/deliveries")]
[Authorize(Policy = Policies.Livraisons)]
public sealed class DeliveriesController(
    DeliveryDbContext context,
    DeliveryWorkflow workflow,
    ICurrentUser currentUser)
    : ControllerBase
{
    /// <summary>Decrit le cycle de vie des livraisons : etats et transitions.</summary>
    [HttpGet("workflow")]
    [AllowAnonymous]
    public ActionResult<object> Workflow() => Ok(new
    {
        etats = Enum.GetValues<DeliveryStatus>().Select(status => new
        {
            etat = status.ToString(),
            transitions = DeliveryStateMachine.NextStates(status).Select(next => next.ToString()),
            final = DeliveryStateMachine.IsFinal(status),
            affectationPossible = DeliveryStateMachine.AllowsAssignment(status)
        })
    });

    /// <summary>Liste paginee des livraisons.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<DeliveryResponse>>> List(
        [FromQuery] DeliveryQuery query,
        CancellationToken cancellationToken)
    {
        var deliveries = context.Deliveries
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .AsQueryable();

        // Un livreur ne voit que ses tournees.
        if (!currentUser.IsBackOffice)
        {
            var courierId = currentUser.CourierId
                            ?? throw new ForbiddenException(
                                "Votre compte n'est rattache a aucun livreur.");

            deliveries = deliveries.Where(d => d.CourierId == courierId);
        }
        else if (query.CourierId is { } filtered)
        {
            deliveries = deliveries.Where(d => d.CourierId == filtered);
        }

        if (query.Unassigned == true)
        {
            deliveries = deliveries.Where(d => d.CourierId == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            deliveries = deliveries.Where(d =>
                EF.Functions.Like(d.Reference, $"%{search}%")
                || EF.Functions.Like(d.OrderNumber, $"%{search}%")
                || EF.Functions.Like(d.CustomerName, $"%{search}%"));
        }

        if (query.Status is { } status)
        {
            deliveries = deliveries.Where(d => d.Status == status);
        }

        var page = await deliveries
            .OrderByDescending(d => d.CreatedAt)
            .ToPageAsync(query, DeliveryMapper.ToResponse, cancellationToken);

        return Ok(page);
    }

    /// <summary>Renvoie un bordereau, contenu et suivi compris.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeliveryResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await workflow.LoadAsync(id, cancellationToken);

        if (!currentUser.IsBackOffice && delivery.CourierId != currentUser.CourierId)
        {
            throw new ForbiddenException($"La livraison {delivery.Reference} n'est pas affectee a votre compte.");
        }

        return Ok(DeliveryMapper.ToResponse(delivery));
    }

    /// <summary>
    /// Cree un bordereau. Appele par order-service a l'expedition d'une commande.
    /// </summary>
    /// <param name="request">Donnees de la commande expediee.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="201">Bordereau cree.</response>
    /// <response code="200">Bordereau deja existant pour cette commande.</response>
    [HttpPost]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<DeliveryResponse>> Create(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var (delivery, created) = await workflow.CreateAsync(request, cancellationToken);

        var response = DeliveryMapper.ToResponse(delivery);

        return created
            ? CreatedAtAction(nameof(Get), new { id = delivery.Id }, response)
            : Ok(response);
    }

    /// <summary>Affecte un livreur.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="request">Livreur a affecter.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<DeliveryResponse>> Assign(
        Guid id,
        AssignCourierRequest request,
        CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(
            await workflow.AssignAsync(id, request.CourierId, cancellationToken)));

    /// <summary>Enregistre l'enlevement : la livraison part en tournee.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="request">Commentaire du livreur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/pickup")]
    public async Task<ActionResult<DeliveryResponse>> PickUp(
        Guid id,
        CommentRequest request,
        CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(
            await workflow.PickUpAsync(id, request.Comment, cancellationToken)));

    /// <summary>Enregistre la remise au client et previent order-service.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="request">Personne ayant receptionne.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/complete")]
    public async Task<ActionResult<DeliveryResponse>> Complete(
        Guid id,
        CompleteDeliveryRequest request,
        CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(
            await workflow.CompleteAsync(id, request.ReceivedBy, cancellationToken)));

    /// <summary>Enregistre une tentative infructueuse.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="request">Motif de l'echec.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/fail")]
    public async Task<ActionResult<DeliveryResponse>> Fail(
        Guid id,
        FailDeliveryRequest request,
        CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(
            await workflow.FailAsync(id, request.Reason, cancellationToken)));

    /// <summary>Annule un bordereau.</summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="request">Motif de l'annulation.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<DeliveryResponse>> Cancel(
        Guid id,
        CommentRequest request,
        CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(
            await workflow.CancelAsync(id, request.Comment, cancellationToken)));

    /// <summary>
    /// Reessaie de prevenir order-service qu'une livraison est faite. Utile si le
    /// conteneur order-service etait indisponible au moment de la remise.
    /// </summary>
    /// <param name="id">Identifiant du bordereau.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/notify-order")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<DeliveryResponse>> NotifyOrder(Guid id, CancellationToken cancellationToken)
        => Ok(DeliveryMapper.ToResponse(await workflow.NotifyOrderAsync(id, cancellationToken)));
}

/// <summary>Projections des livraisons vers les formats exposes par l'API.</summary>
public static class DeliveryMapper
{
    /// <summary>Convertit un bordereau.</summary>
    /// <param name="delivery">Bordereau, livreur et lignes charges si disponibles.</param>
    public static DeliveryResponse ToResponse(Delivery delivery) => new()
    {
        Id = delivery.Id,
        Reference = delivery.Reference,
        OrderId = delivery.OrderId,
        OrderNumber = delivery.OrderNumber,
        CustomerId = delivery.CustomerId,
        CustomerName = delivery.CustomerName,
        CustomerPhone = delivery.CustomerPhone,
        ShippingAddress = delivery.ShippingAddress,
        ShippingCity = delivery.ShippingCity,
        Status = delivery.Status,
        AllowedTransitions = DeliveryStateMachine.NextStates(delivery.Status)
            .Select(status => status.ToString())
            .ToList(),
        CourierId = delivery.CourierId,
        CourierName = delivery.Courier?.FullName,
        ReceivedBy = delivery.ReceivedBy,
        FailureReason = delivery.FailureReason,
        AttemptCount = delivery.AttemptCount,
        OrderNotified = delivery.OrderNotified,
        CreatedAt = delivery.CreatedAt,
        AssignedAt = delivery.AssignedAt,
        PickedUpAt = delivery.PickedUpAt,
        DeliveredAt = delivery.DeliveredAt,
        Lines = delivery.Lines
            .OrderBy(line => line.Id)
            .Select(line => new DeliveryLineResponse
            {
                ProductId = line.ProductId,
                Sku = line.Sku,
                ProductName = line.ProductName,
                Quantity = line.Quantity
            })
            .ToList(),
        Tracking = delivery.Tracking
            .OrderBy(entry => entry.ChangedAt)
            .Select(entry => new TrackingResponse
            {
                FromStatus = entry.FromStatus,
                ToStatus = entry.ToStatus,
                Comment = entry.Comment,
                ChangedBy = entry.ChangedBy,
                ChangedAt = entry.ChangedAt
            })
            .ToList()
    };
}
