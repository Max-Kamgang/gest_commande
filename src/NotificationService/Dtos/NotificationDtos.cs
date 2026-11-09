using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.NotificationService.Domain;

namespace GestionCommandes.NotificationService.Dtos;

/// <summary>Notification exposee par l'API.</summary>
public sealed class NotificationResponse
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? Recipient { get; set; }
    public NotificationStatus Status { get; set; }
    public string? DeliveryLog { get; set; }
    public int SendAttempts { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string>? Data { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Accuse de reception renvoye au service producteur.</summary>
public sealed class EventAcceptedResponse
{
    /// <summary>Identifiant de l'evenement traite.</summary>
    public Guid EventId { get; set; }

    /// <summary>Identifiant de la notification creee ou retrouvee.</summary>
    public Guid NotificationId { get; set; }

    /// <summary>Vrai si l'evenement avait deja ete recu (rejeu).</summary>
    public bool Duplicate { get; set; }

    /// <summary>Etat de la notification apres traitement.</summary>
    public NotificationStatus Status { get; set; }
}

/// <summary>Filtres de recherche de notifications.</summary>
public sealed class NotificationQuery : PageQuery
{
    [MaxLength(60)]
    public string? EventType { get; set; }

    [MaxLength(20)]
    public string? Severity { get; set; }

    public NotificationStatus? Status { get; set; }

    /// <summary>Filtre par sujet metier exact (numero de commande, reference produit).</summary>
    [MaxLength(80)]
    public string? Subject { get; set; }

    [MaxLength(160)]
    public string? Recipient { get; set; }

    /// <summary>Recherche libre sur le titre et le message.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Ne renvoyer que les notifications non lues.</summary>
    public bool? UnreadOnly { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }
}

/// <summary>Synthese pour un tableau de bord.</summary>
public sealed class NotificationSummaryResponse
{
    public int Total { get; set; }
    public int Unread { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
    public int Failed { get; set; }
    public DateTimeOffset? LastReceivedAt { get; set; }

    /// <summary>Repartition par type d'evenement.</summary>
    public Dictionary<string, int> ByEventType { get; set; } = [];

    /// <summary>Repartition par service emetteur.</summary>
    public Dictionary<string, int> BySource { get; set; } = [];
}
