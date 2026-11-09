using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.NotificationService.Domain;

/// <summary>Etat d'une notification.</summary>
public enum NotificationStatus
{
    /// <summary>Evenement recu, diffusion pas encore tentee.</summary>
    Recue = 1,

    /// <summary>Diffusion simulee effectuee avec succes.</summary>
    Envoyee = 2,

    /// <summary>La diffusion simulee a echoue.</summary>
    Echouee = 3,

    /// <summary>Notification consultee par un utilisateur.</summary>
    Lue = 4
}

/// <summary>
/// Notification issue d'un evenement d'integration.
///
/// L'identifiant d'evenement est unique : un reenvoi du meme evenement par un
/// service producteur ne cree pas de doublon. C'est l'idempotence cote consommateur.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identifiant de l'evenement d'origine. Unique : cle d'idempotence.</summary>
    public Guid EventId { get; set; }

    /// <summary>Type d'evenement (<c>order.confirmed</c>, <c>inventory.stock-low</c>...).</summary>
    [MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Service emetteur.</summary>
    [MaxLength(60)]
    public string Source { get; set; } = string.Empty;

    /// <summary>Sujet metier (numero de commande, reference produit...).</summary>
    [MaxLength(80)]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gravite : <c>Info</c>, <c>Warning</c> ou <c>Critical</c>.</summary>
    [MaxLength(20)]
    public string Severity { get; set; } = "Info";

    /// <summary>Canal de diffusion : <c>Email</c>, <c>Sms</c> ou <c>Interne</c>.</summary>
    [MaxLength(20)]
    public string Channel { get; set; } = "Interne";

    /// <summary>Destinataire (adresse email ou numero).</summary>
    [MaxLength(160)]
    public string? Recipient { get; set; }

    public NotificationStatus Status { get; set; } = NotificationStatus.Recue;

    /// <summary>Compte rendu de la diffusion simulee (fichier produit, erreur...).</summary>
    [MaxLength(500)]
    public string? DeliveryLog { get; set; }

    /// <summary>Nombre de tentatives de diffusion.</summary>
    public int SendAttempts { get; set; }

    /// <summary>Correlation-ID de la requete qui a produit l'evenement.</summary>
    [MaxLength(128)]
    public string? CorrelationId { get; set; }

    /// <summary>Donnees techniques de l'evenement, conservees en JSON.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>Horodatage de l'evenement chez le producteur.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Horodatage de reception par notification-service.</summary>
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
