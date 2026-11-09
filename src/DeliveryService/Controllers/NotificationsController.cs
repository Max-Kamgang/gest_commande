using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Controllers;

/// <summary>
/// Notifications de suivi. Elles sont produites par ce service a chaque
/// changement d'etat d'une livraison ; l'endpoint de creation reste ouvert aux
/// appels internes pour qu'un autre service puisse publier les siennes.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = Policies.BackOffice)]
public sealed class NotificationsController(DeliveryDbContext context) : ControllerBase
{
    /// <summary>Liste paginee des notifications, les plus recentes d'abord.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationResponse>>> List(
        [FromQuery] NotificationQuery query,
        CancellationToken cancellationToken)
    {
        var notifications = context.Notifications.AsQueryable();

        if (query.UnreadOnly == true)
        {
            notifications = notifications.Where(n => !n.IsRead);
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            var subject = query.Subject.Trim();
            notifications = notifications.Where(n => n.Subject == subject);
        }

        var page = await notifications
            .OrderByDescending(n => n.CreatedAt)
            .ToPageAsync(query, ToResponse, cancellationToken);

        return Ok(page);
    }

    /// <summary>Compteurs pour le bandeau de l'interface.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("summary")]
    public async Task<ActionResult<NotificationSummaryResponse>> Summary(CancellationToken cancellationToken) =>
        Ok(new NotificationSummaryResponse
        {
            Total = await context.Notifications.CountAsync(cancellationToken),
            Unread = await context.Notifications.CountAsync(n => !n.IsRead, cancellationToken),
            Warnings = await context.Notifications.CountAsync(
                n => !n.IsRead && n.Severity == NotificationSeverity.Avertissement, cancellationToken)
        });

    /// <summary>Publie une notification. Reserve aux appels internes entre services.</summary>
    /// <param name="request">Contenu de la notification.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    [Authorize(Roles = Roles.Service)]
    public async Task<ActionResult<NotificationResponse>> Create(
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var notification = new Notification
        {
            EventType = request.EventType.Trim(),
            Subject = request.Subject.Trim(),
            Title = request.Title.Trim(),
            Message = request.Message.Trim(),
            Severity = request.Severity,
            Recipient = request.Recipient?.Trim()
        };

        context.Notifications.Add(notification);
        await context.SaveChangesAsync(cancellationToken);

        return Created($"/api/notifications/{notification.Id}", ToResponse(notification));
    }

    /// <summary>Marque une notification comme lue.</summary>
    /// <param name="id">Identifiant de la notification.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<NotificationResponse>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                           ?? throw new NotFoundException("La notification", id);

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
        }

        return Ok(ToResponse(notification));
    }

    /// <summary>Marque toutes les notifications comme lues.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("read-all")]
    public async Task<ActionResult<NotificationSummaryResponse>> MarkAllRead(CancellationToken cancellationToken)
    {
        var unread = await context.Notifications.Where(n => !n.IsRead).ToListAsync(cancellationToken);

        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTimeOffset.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);

        return await Summary(cancellationToken);
    }

    private static NotificationResponse ToResponse(Notification notification) => new()
    {
        Id = notification.Id,
        EventType = notification.EventType,
        Subject = notification.Subject,
        Title = notification.Title,
        Message = notification.Message,
        Severity = notification.Severity,
        Recipient = notification.Recipient,
        IsRead = notification.IsRead,
        CreatedAt = notification.CreatedAt,
        ReadAt = notification.ReadAt
    };
}
