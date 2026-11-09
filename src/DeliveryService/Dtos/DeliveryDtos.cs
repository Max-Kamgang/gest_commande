using System.ComponentModel.DataAnnotations;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.Shared.Web;

namespace GestionCommandes.DeliveryService.Dtos;

// ================================= Livreurs =================================

/// <summary>Livreur expose par l'API.</summary>
public sealed class CourierResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Code livreur.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Nom complet.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    public string? Phone { get; set; }

    /// <summary>Moyen de transport.</summary>
    public string Vehicle { get; set; } = string.Empty;

    /// <summary>Zone de tournee.</summary>
    public string? Zone { get; set; }

    /// <summary>Livreur actif.</summary>
    public bool IsActive { get; set; }

    /// <summary>Livraisons en cours (affectees ou en route).</summary>
    public int ActiveDeliveries { get; set; }
}

/// <summary>Creation ou mise a jour d'un livreur.</summary>
public sealed class CourierRequest
{
    /// <summary>Code livreur unique.</summary>
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Nom complet.</summary>
    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>Moyen de transport. « Moto » s'il n'est pas precise.</summary>
    [MaxLength(40)]
    public string? Vehicle { get; set; }

    /// <summary>Zone de tournee.</summary>
    [MaxLength(80)]
    public string? Zone { get; set; }
}

// ================================ Livraisons ================================

/// <summary>Ligne de bordereau exposee par l'API.</summary>
public sealed class DeliveryLineResponse
{
    /// <summary>Produit.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article.</summary>
    public string? Sku { get; set; }

    /// <summary>Libelle produit.</summary>
    public string? ProductName { get; set; }

    /// <summary>Quantite.</summary>
    public int Quantity { get; set; }
}

/// <summary>Etape de suivi exposee par l'API.</summary>
public sealed class TrackingResponse
{
    /// <summary>Etat de depart.</summary>
    public DeliveryStatus? FromStatus { get; set; }

    /// <summary>Nouvel etat.</summary>
    public DeliveryStatus ToStatus { get; set; }

    /// <summary>Commentaire.</summary>
    public string? Comment { get; set; }

    /// <summary>Auteur.</summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Horodatage.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>Bordereau de livraison expose par l'API.</summary>
public sealed class DeliveryResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Reference lisible.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Commande d'origine.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Numero de commande.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Client.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Nom du client.</summary>
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Telephone du client.</summary>
    public string? CustomerPhone { get; set; }

    /// <summary>Adresse de livraison.</summary>
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    public string? ShippingCity { get; set; }

    /// <summary>Etat courant.</summary>
    public DeliveryStatus Status { get; set; }

    /// <summary>Etats atteignables depuis l'etat courant.</summary>
    public IReadOnlyList<string> AllowedTransitions { get; set; } = [];

    /// <summary>Livreur affecte.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Nom du livreur affecte.</summary>
    public string? CourierName { get; set; }

    /// <summary>Personne ayant receptionne.</summary>
    public string? ReceivedBy { get; set; }

    /// <summary>Motif du dernier echec.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Nombre de tentatives infructueuses.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Vrai une fois order-service averti de la livraison effective.</summary>
    public bool OrderNotified { get; set; }

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date d'affectation.</summary>
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>Date d'enlevement.</summary>
    public DateTimeOffset? PickedUpAt { get; set; }

    /// <summary>Date de remise.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Contenu du bordereau.</summary>
    public List<DeliveryLineResponse> Lines { get; set; } = [];

    /// <summary>Historique de suivi.</summary>
    public List<TrackingResponse> Tracking { get; set; } = [];
}

/// <summary>Ligne du bordereau envoyee par order-service.</summary>
public sealed class CreateDeliveryLineRequest
{
    /// <summary>Produit.</summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Reference article.</summary>
    [MaxLength(40)]
    public string? Sku { get; set; }

    /// <summary>Libelle produit.</summary>
    [MaxLength(160)]
    public string? ProductName { get; set; }

    /// <summary>Quantite.</summary>
    [Range(1, 100_000)]
    public int Quantity { get; set; }
}

/// <summary>
/// Creation d'un bordereau, appelee par order-service a l'expedition.
/// </summary>
public sealed class CreateDeliveryRequest
{
    /// <summary>Commande d'origine.</summary>
    [Required]
    public Guid OrderId { get; set; }

    /// <summary>Numero de commande : cle de non-doublon.</summary>
    [Required, MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Client.</summary>
    [Required]
    public Guid CustomerId { get; set; }

    /// <summary>Nom du client.</summary>
    [Required, MaxLength(160)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Telephone du client.</summary>
    [MaxLength(30)]
    public string? CustomerPhone { get; set; }

    /// <summary>Adresse de livraison.</summary>
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    /// <summary>Contenu a livrer.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins une ligne est requise.")]
    public List<CreateDeliveryLineRequest> Lines { get; set; } = [];
}

/// <summary>Affectation d'un livreur.</summary>
public sealed class AssignCourierRequest
{
    /// <summary>Livreur a affecter.</summary>
    [Required]
    public Guid CourierId { get; set; }
}

/// <summary>Remise au client.</summary>
public sealed class CompleteDeliveryRequest
{
    /// <summary>Nom de la personne qui a receptionne.</summary>
    [Required(ErrorMessage = "receivedBy est obligatoire."), MaxLength(160)]
    public string ReceivedBy { get; set; } = string.Empty;
}

/// <summary>Tentative de livraison infructueuse.</summary>
public sealed class FailDeliveryRequest
{
    /// <summary>Motif de l'echec.</summary>
    [Required(ErrorMessage = "reason est obligatoire."), MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Commentaire libre, utilise a l'enlevement et a l'annulation.</summary>
public sealed class CommentRequest
{
    /// <summary>Commentaire.</summary>
    [MaxLength(300)]
    public string? Comment { get; set; }
}

/// <summary>Filtres de recherche de livraisons.</summary>
public sealed class DeliveryQuery : PageQuery
{
    /// <summary>Recherche libre sur la reference, le numero de commande et le client.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Filtre par etat.</summary>
    public DeliveryStatus? Status { get; set; }

    /// <summary>Filtre par livreur.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Ne garder que les bordereaux sans livreur.</summary>
    public bool? Unassigned { get; set; }
}

// =============================== Notifications ===============================

/// <summary>Notification exposee par l'API.</summary>
public sealed class NotificationResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Type d'evenement.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Sujet metier.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Titre.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gravite.</summary>
    public NotificationSeverity Severity { get; set; }

    /// <summary>Destinataire.</summary>
    public string? Recipient { get; set; }

    /// <summary>Notification lue.</summary>
    public bool IsRead { get; set; }

    /// <summary>Date de production.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date de lecture.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>Publication d'une notification par un appel interne.</summary>
public sealed class CreateNotificationRequest
{
    /// <summary>Type d'evenement.</summary>
    [Required, MaxLength(60)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Sujet metier.</summary>
    [Required, MaxLength(80)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>Titre.</summary>
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Message.</summary>
    [Required, MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Gravite.</summary>
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    /// <summary>Destinataire affiche.</summary>
    [MaxLength(160)]
    public string? Recipient { get; set; }
}

/// <summary>Filtres de recherche de notifications.</summary>
public sealed class NotificationQuery : PageQuery
{
    /// <summary>Ne garder que les non lues.</summary>
    public bool? UnreadOnly { get; set; }

    /// <summary>Filtre par sujet (numero de commande ou de bordereau).</summary>
    [MaxLength(80)]
    public string? Subject { get; set; }
}

/// <summary>Compteurs affiches dans le bandeau de l'interface.</summary>
public sealed class NotificationSummaryResponse
{
    /// <summary>Nombre total de notifications.</summary>
    public int Total { get; set; }

    /// <summary>Nombre de notifications non lues.</summary>
    public int Unread { get; set; }

    /// <summary>Nombre d'avertissements non lus.</summary>
    public int Warnings { get; set; }
}
