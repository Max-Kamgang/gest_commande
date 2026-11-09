using System.Text.Json;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.NotificationService.Data;
using GestionCommandes.NotificationService.Domain;
using GestionCommandes.NotificationService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.NotificationService.Services;

/// <summary>Reception des evenements et consultation des notifications.</summary>
public sealed class NotificationManager(
    NotificationDbContext context,
    INotificationSender sender,
    ICurrentUser currentUser,
    ILogger<NotificationManager> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// Traite un evenement d'integration : stockage puis diffusion simulee.
    /// Idempotent sur <c>eventId</c> : un rejeu du producteur ne cree pas de doublon.
    /// </summary>
    public async Task<EventAcceptedResponse> HandleEventAsync(
        IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        if (integrationEvent.EventId == Guid.Empty)
        {
            throw new BadRequestException("eventId est obligatoire.", "EVENT_ID_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(integrationEvent.EventType))
        {
            throw new BadRequestException("eventType est obligatoire.", "EVENT_TYPE_REQUIRED");
        }

        var existing = await context.Notifications
            .FirstOrDefaultAsync(n => n.EventId == integrationEvent.EventId, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Evenement {EventId} ({EventType}) deja recu : reponse idempotente",
                integrationEvent.EventId,
                integrationEvent.EventType);

            return new EventAcceptedResponse
            {
                EventId = existing.EventId,
                NotificationId = existing.Id,
                Duplicate = true,
                Status = existing.Status
            };
        }

        var notification = new Notification
        {
            EventId = integrationEvent.EventId,
            EventType = integrationEvent.EventType.Trim(),
            Source = string.IsNullOrWhiteSpace(integrationEvent.Source) ? "inconnu" : integrationEvent.Source.Trim(),
            Subject = integrationEvent.Subject?.Trim() ?? string.Empty,
            Title = string.IsNullOrWhiteSpace(integrationEvent.Title)
                ? integrationEvent.EventType
                : integrationEvent.Title.Trim(),
            Message = integrationEvent.Message?.Trim() ?? string.Empty,
            Severity = Normalize(integrationEvent.Severity, EventSeverities.Info),
            Channel = Normalize(integrationEvent.Channel, EventChannels.Interne),
            Recipient = integrationEvent.Recipient?.Trim(),
            CorrelationId = integrationEvent.CorrelationId,
            PayloadJson = integrationEvent.Data.Count > 0
                ? JsonSerializer.Serialize(integrationEvent.Data, JsonOptions)
                : null,
            OccurredAt = integrationEvent.OccurredAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            Status = NotificationStatus.Recue
        };

        // Diffusion simulee (email / SMS ecrit dans le dossier outbox du volume).
        var result = await sender.SendAsync(notification, cancellationToken);

        notification.SendAttempts = 1;
        notification.DeliveryLog = result.Log;
        notification.Status = result.Success ? NotificationStatus.Envoyee : NotificationStatus.Echouee;
        notification.SentAt = result.Success ? DateTimeOffset.UtcNow : null;

        context.Notifications.Add(notification);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Course entre deux livraisons du meme evenement : la premiere a gagne.
            context.ChangeTracker.Clear();

            var winner = await context.Notifications
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.EventId == integrationEvent.EventId, cancellationToken);

            if (winner is not null)
            {
                return new EventAcceptedResponse
                {
                    EventId = winner.EventId,
                    NotificationId = winner.Id,
                    Duplicate = true,
                    Status = winner.Status
                };
            }

            throw;
        }

        logger.LogInformation(
            "Evenement {EventType} de {Source} enregistre : notification {NotificationId} ({Status})",
            notification.EventType,
            notification.Source,
            notification.Id,
            notification.Status);

        return new EventAcceptedResponse
        {
            EventId = notification.EventId,
            NotificationId = notification.Id,
            Duplicate = false,
            Status = notification.Status
        };
    }

    /// <summary>Liste paginee des notifications, avec filtres.</summary>
    public async Task<PagedResult<NotificationResponse>> SearchAsync(
        NotificationQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new BadRequestException("from ne peut pas etre posterieur a to.", "INVALID_DATE_RANGE");
        }

        var source = context.Notifications.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            source = source.Where(n => n.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = query.Severity.Trim();
            source = source.Where(n => n.Severity == severity);
        }

        if (query.Status is { } status)
        {
            source = source.Where(n => n.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            var subject = query.Subject.Trim();
            source = source.Where(n => n.Subject == subject);
        }

        if (!string.IsNullOrWhiteSpace(query.Recipient))
        {
            var recipient = query.Recipient.Trim();
            source = source.Where(n => n.Recipient == recipient);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(n =>
                EF.Functions.Like(n.Title, $"%{term}%") ||
                EF.Functions.Like(n.Message, $"%{term}%") ||
                EF.Functions.Like(n.Subject, $"%{term}%"));
        }

        if (query.UnreadOnly == true)
        {
            source = source.Where(n => n.Status != NotificationStatus.Lue);
        }

        if (query.From is { } fromDate)
        {
            source = source.Where(n => n.ReceivedAt >= fromDate);
        }

        if (query.To is { } toDate)
        {
            source = source.Where(n => n.ReceivedAt <= toDate);
        }

        return await source
            .OrderByDescending(n => n.ReceivedAt)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere une notification.</summary>
    public async Task<NotificationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new NotFoundException("La notification", id);

        return ToResponse(notification);
    }

    /// <summary>Marque une notification comme lue.</summary>
    public async Task<NotificationResponse> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                           ?? throw new NotFoundException("La notification", id);

        if (notification.Status == NotificationStatus.Lue)
        {
            return ToResponse(notification);
        }

        notification.Status = NotificationStatus.Lue;
        notification.ReadAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Notification {NotificationId} marquee lue par {User}",
            id,
            currentUser.UserName ?? "inconnu");

        return ToResponse(notification);
    }

    /// <summary>Marque toutes les notifications non lues comme lues.</summary>
    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var count = await context.Notifications
            .Where(n => n.Status != NotificationStatus.Lue)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(n => n.Status, NotificationStatus.Lue)
                    .SetProperty(n => n.ReadAt, now),
                cancellationToken);

        logger.LogInformation(
            "{Count} notification(s) marquee(s) lue(s) par {User}",
            count,
            currentUser.UserName ?? "inconnu");

        return count;
    }

    /// <summary>Relance la diffusion simulee d'une notification en echec.</summary>
    public async Task<NotificationResponse> ResendAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                           ?? throw new NotFoundException("La notification", id);

        var result = await sender.SendAsync(notification, cancellationToken);

        notification.SendAttempts++;
        notification.DeliveryLog = result.Log;

        if (result.Success)
        {
            notification.Status = NotificationStatus.Envoyee;
            notification.SentAt = DateTimeOffset.UtcNow;
        }
        else
        {
            notification.Status = NotificationStatus.Echouee;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Notification {NotificationId} rediffusee (tentative {Attempt}) : {Status}",
            id,
            notification.SendAttempts,
            notification.Status);

        return ToResponse(notification);
    }

    /// <summary>Supprime une notification.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await context.Notifications.FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
                           ?? throw new NotFoundException("La notification", id);

        context.Notifications.Remove(notification);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Notification {NotificationId} supprimee", id);
    }

    /// <summary>Synthese pour le tableau de bord.</summary>
    public async Task<NotificationSummaryResponse> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var all = await context.Notifications
            .AsNoTracking()
            .Select(n => new { n.Severity, n.Status, n.EventType, n.Source, n.ReceivedAt })
            .ToListAsync(cancellationToken);

        return new NotificationSummaryResponse
        {
            Total = all.Count,
            Unread = all.Count(n => n.Status != NotificationStatus.Lue),
            Critical = all.Count(n => n.Severity == EventSeverities.Critical),
            Warning = all.Count(n => n.Severity == EventSeverities.Warning),
            Info = all.Count(n => n.Severity == EventSeverities.Info),
            Failed = all.Count(n => n.Status == NotificationStatus.Echouee),
            LastReceivedAt = all.Count == 0 ? null : all.Max(n => n.ReceivedAt),
            ByEventType = all
                .GroupBy(n => n.EventType)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count()),
            BySource = all
                .GroupBy(n => n.Source)
                .OrderByDescending(g => g.Count())
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    private static string Normalize(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static NotificationResponse ToResponse(Notification notification) => new()
    {
        Id = notification.Id,
        EventId = notification.EventId,
        EventType = notification.EventType,
        Source = notification.Source,
        Subject = notification.Subject,
        Title = notification.Title,
        Message = notification.Message,
        Severity = notification.Severity,
        Channel = notification.Channel,
        Recipient = notification.Recipient,
        Status = notification.Status,
        DeliveryLog = notification.DeliveryLog,
        SendAttempts = notification.SendAttempts,
        CorrelationId = notification.CorrelationId,
        Data = string.IsNullOrWhiteSpace(notification.PayloadJson)
            ? null
            : TryParse(notification.PayloadJson),
        OccurredAt = notification.OccurredAt,
        ReceivedAt = notification.ReceivedAt,
        SentAt = notification.SentAt,
        ReadAt = notification.ReadAt
    };

    private static Dictionary<string, string>? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
