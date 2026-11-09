using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.NotificationService.Dtos;
using GestionCommandes.NotificationService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.NotificationService.Controllers;

/// <summary>Consultation des notifications.</summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[Authorize]
public sealed class NotificationsController(NotificationManager notifications) : ControllerBase
{
    /// <summary>Liste paginee des notifications, avec filtres.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<NotificationResponse>>> Search(
        [FromQuery] NotificationQuery query,
        CancellationToken cancellationToken)
        => Ok(await notifications.SearchAsync(query, cancellationToken));

    /// <summary>Synthese par gravite, type et service emetteur (tableau de bord).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<NotificationSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationSummaryResponse>> GetSummary(CancellationToken cancellationToken)
        => Ok(await notifications.GetSummaryAsync(cancellationToken));

    /// <summary>Recupere une notification.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await notifications.GetAsync(id, cancellationToken));

    /// <summary>Marque une notification comme lue.</summary>
    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken cancellationToken)
        => Ok(await notifications.MarkReadAsync(id, cancellationToken));

    /// <summary>Marque toutes les notifications non lues comme lues.</summary>
    [HttpPost("read-all")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> MarkAllRead(CancellationToken cancellationToken)
        => Ok(new { updated = await notifications.MarkAllReadAsync(cancellationToken) });

    /// <summary>Relance la diffusion simulee d'une notification en echec. Reserve au back-office.</summary>
    [HttpPost("{id:guid}/resend")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationResponse>> Resend(Guid id, CancellationToken cancellationToken)
        => Ok(await notifications.ResendAsync(id, cancellationToken));

    /// <summary>Supprime une notification. Reserve au back-office.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await notifications.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
