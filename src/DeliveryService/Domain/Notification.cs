using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.DeliveryService.Domain;

/// <summary>Gravite d'une notification.</summary>
public enum NotificationSeverity
{
    /// <summary>Information courante.</summary>
    Info = 1,

    /// <summary>Point d'attention : tentative de livraison echouee, par exemple.</summary>
    Avertissement = 2
}

/// <summary>
/// Notification destinee au suivi : « bordereau cree », « livreur affecte »,
/// « colis remis », « tentative echouee ».
///
/// Ce service produit ces messages lui-meme a chaque changement d'etat d'une
/// livraison. L'endpoint <c>POST /api/notifications</c> reste ouvert aux appels
/// internes : c'est par la qu'un autre service pourra publier ses propres
/// evenements sans que rien ne change ici.
/// </summary>
public class Notification
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Type d'evenement, par exemple <c>delivery.assigned</c>.</summary>
    [MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Sujet metier : numero de commande ou reference de bordereau.</summary>
    [MaxLength(80)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Titre court.</summary>
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Corps du message.</summary>
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gravite.</summary>
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>Destinataire affiche (email du client, par exemple).</summary>
    [MaxLength(160)]
    public string? Recipient { get; set; }

    /// <summary>Vrai une fois la notification consultee.</summary>
    public bool IsRead { get; set; }

    /// <summary>Date de production.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de lecture.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}
