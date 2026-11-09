using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.NotificationService.Dtos;
using GestionCommandes.NotificationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.NotificationService.Controllers;

/// <summary>
/// Reception des evenements d'integration publies par les autres microservices
/// (commande confirmee, stock bas, livraison effectuee...).
///
/// L'enveloppe <see cref="IntegrationEvent"/> est le seul contrat partage de la
/// plateforme : elle est definie une fois dans la bibliotheque transverse, car
/// tous les services la produisent et notification-service la consomme.
/// </summary>
[ApiController]
[Route("api/events")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.BackOfficeOuService)]
public sealed class EventsController(NotificationManager notifications) : ControllerBase
{
    /// <summary>
    /// Enregistre un evenement et declenche la diffusion simulee (email / SMS).
    /// Idempotent sur <c>eventId</c>.
    /// </summary>
    /// <response code="202">Evenement accepte et traite.</response>
    /// <response code="400">Enveloppe d'evenement invalide.</response>
    [HttpPost]
    [ProducesResponseType<EventAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventAcceptedResponse>> Receive(
        [FromBody] IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var result = await notifications.HandleEventAsync(integrationEvent, cancellationToken);

        if (result.Duplicate)
        {
            Response.Headers["Idempotent-Replay"] = "true";
        }

        return Accepted(result);
    }

    /// <summary>Liste les types d'evenements reconnus par la plateforme.</summary>
    [HttpGet("types")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetTypes() => Ok(new
    {
        types = new[]
        {
            EventTypes.OrderConfirmed,
            EventTypes.OrderShipped,
            EventTypes.OrderCancelled,
            EventTypes.OrderDelivered,
            EventTypes.StockLow,
            EventTypes.StockMovement,
            EventTypes.DeliveryAssigned,
            EventTypes.DeliveryCompleted
        },
        severities = new[] { EventSeverities.Info, EventSeverities.Warning, EventSeverities.Critical },
        channels = new[] { EventChannels.Email, EventChannels.Sms, EventChannels.Interne }
    });
}
