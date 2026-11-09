using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.DeliveryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.DeliveryService.Controllers;

/// <summary>
/// Livraisons : creation par order-service, affectation d'un livreur, suivi des
/// statuts et historique.
///
/// Un compte de role Livreur ne voit et ne met a jour que les livraisons qui lui
/// sont affectees (claim <c>courier_id</c> du jeton).
/// </summary>
[ApiController]
[Route("api/deliveries")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Livraisons)]
public sealed class DeliveriesController(DeliveryManager deliveries) : ControllerBase
{
    /// <summary>Liste paginee des livraisons, avec filtres.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DeliveryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<DeliveryResponse>>> Search(
        [FromQuery] DeliveryQuery query,
        CancellationToken cancellationToken)
        => Ok(await deliveries.SearchAsync(query, cancellationToken));

    /// <summary>
    /// Raccourci : les livraisons du livreur connecte.
    /// Equivalent a <c>GET /api/deliveries</c> pour un compte de role Livreur.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType<PagedResult<DeliveryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeliveryResponse>>> Mine(
        [FromQuery] DeliveryQuery query,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        // Pour un gestionnaire, « mine » n'a pas de sens : on renvoie tout.
        query.CourierId = currentUser.CourierId ?? query.CourierId;

        return Ok(await deliveries.SearchAsync(query, cancellationToken));
    }

    /// <summary>Recupere une livraison.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeliveryResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await deliveries.GetAsync(id, cancellationToken));

    /// <summary>Recupere la livraison d'une commande.</summary>
    [HttpGet("by-order/{orderNumber}")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeliveryResponse>> GetByOrder(
        string orderNumber,
        CancellationToken cancellationToken)
        => Ok(await deliveries.GetByOrderNumberAsync(orderNumber, cancellationToken));

    /// <summary>Historique de suivi d'une livraison.</summary>
    [HttpGet("{id:guid}/tracking")]
    [ProducesResponseType<IReadOnlyList<TrackingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TrackingResponse>>> GetTracking(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await deliveries.GetTrackingAsync(id, cancellationToken));

    /// <summary>
    /// Cree un bordereau de livraison. Appele par order-service a l'expedition
    /// de la commande (appel interne de service a service).
    /// Idempotent sur le numero de commande.
    /// </summary>
    /// <response code="201">Bordereau cree.</response>
    /// <response code="200">Bordereau deja existant : reponse idempotente.</response>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.BackOfficeOuService)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DeliveryResponse>> Create(
        [FromBody] CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var (delivery, alreadyExisted) = await deliveries.CreateAsync(request, cancellationToken);

        if (alreadyExisted)
        {
            Response.Headers["Idempotent-Replay"] = "true";

            return Ok(delivery);
        }

        return CreatedAtAction(nameof(Get), new { id = delivery.Id }, delivery);
    }

    /// <summary>Affecte ou reaffecte un livreur. Reserve au back-office.</summary>
    /// <response code="409">Etat incompatible, livreur inactif ou a pleine charge.</response>
    [HttpPost("{id:guid}/assign")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> Assign(
        Guid id,
        [FromBody] AssignCourierRequest request,
        CancellationToken cancellationToken)
        => Ok(await deliveries.AssignAsync(id, request, cancellationToken));

    /// <summary>
    /// Fait evoluer le statut d'une livraison. Un livreur ne peut agir que sur
    /// ses propres livraisons.
    ///
    /// Le passage a <c>Livree</c> exige <c>receivedBy</c> et declenche la mise a
    /// jour de la commande dans order-service.
    /// </summary>
    /// <response code="200">Nouvel etat de la livraison.</response>
    /// <response code="400">Donnee obligatoire manquante (destinataire ou motif d'echec).</response>
    /// <response code="403">La livraison n'est pas affectee a ce livreur.</response>
    /// <response code="409">Transition invalide ou aucun livreur affecte.</response>
    [HttpPost("{id:guid}/status")]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdateDeliveryStatusRequest request,
        CancellationToken cancellationToken)
        => Ok(await deliveries.UpdateStatusAsync(id, request, cancellationToken));

    /// <summary>Annule une livraison. Reserve au back-office.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryResponse>> Cancel(
        Guid id,
        [FromBody] CancelDeliveryRequest request,
        CancellationToken cancellationToken)
        => Ok(await deliveries.CancelAsync(id, request.Reason, cancellationToken));

    /// <summary>
    /// Relance la notification d'order-service pour une livraison deja cloturee
    /// dont la commande n'a pas pu etre mise a jour (order-service momentanement
    /// indisponible). Reserve au back-office.
    /// </summary>
    /// <response code="503">order-service est toujours indisponible.</response>
    [HttpPost("{id:guid}/notify-order")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<DeliveryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DeliveryResponse>> RetryNotification(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await deliveries.RetryOrderNotificationAsync(id, cancellationToken));

    /// <summary>Liste les etats de livraison et les transitions autorisees.</summary>
    [HttpGet("workflow")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetWorkflow() => Ok(new
    {
        states = Enum.GetValues<DeliveryStatus>().Select(s => new
        {
            name = s.ToString(),
            value = (int)s,
            isFinal = DeliveryStateMachine.IsFinal(s),
            allowsAssignment = DeliveryStateMachine.AllowsAssignment(s),
            transitions = DeliveryStateMachine.NextStates(s).Select(n => n.ToString()).ToArray()
        })
    });
}
