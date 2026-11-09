using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.DeliveryService.Domain;

namespace GestionCommandes.DeliveryService.Dtos;

// ------------------------------- Livreurs -------------------------------

/// <summary>Livreur expose par l'API.</summary>
public sealed class CourierResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Vehicle { get; set; } = string.Empty;
    public string? Zone { get; set; }
    public int MaxConcurrentDeliveries { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Livraisons actuellement en charge (affectees ou en cours).</summary>
    public int ActiveDeliveries { get; set; }

    /// <summary>Livraisons menees a bien.</summary>
    public int CompletedDeliveries { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation / mise a jour d'un livreur.</summary>
public sealed class CourierRequest
{
    [Required, MaxLength(20), RegularExpression("^[A-Z0-9-]{3,20}$",
        ErrorMessage = "code doit etre en majuscules (lettres, chiffres, tiret), 3 a 20 caracteres.")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [Phone, MaxLength(30)]
    public string? Phone { get; set; }

    [Required, MaxLength(40)]
    public string Vehicle { get; set; } = "Moto";

    [MaxLength(80)]
    public string? Zone { get; set; }

    [Range(1, 100, ErrorMessage = "maxConcurrentDeliveries doit etre compris entre 1 et 100.")]
    public int MaxConcurrentDeliveries { get; set; } = 10;
}

/// <summary>Filtres de recherche de livreurs.</summary>
public sealed class CourierQuery : PageQuery
{
    [MaxLength(120)]
    public string? Search { get; set; }

    public bool? IsActive { get; set; }
}

/// <summary>Activation / desactivation.</summary>
public sealed class SetActiveRequest
{
    [Required]
    public bool IsActive { get; set; }
}

// ------------------------------ Livraisons ------------------------------

/// <summary>Ligne de bordereau exposee par l'API.</summary>
public sealed class DeliveryLineResponse
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Etape de suivi exposee par l'API.</summary>
public sealed class TrackingResponse
{
    public DeliveryStatus? FromStatus { get; set; }
    public DeliveryStatus ToStatus { get; set; }
    public string? Location { get; set; }
    public string? Comment { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>Livraison exposee par l'API.</summary>
public sealed class DeliveryResponse
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public DeliveryStatus Status { get; set; }

    /// <summary>Transitions possibles depuis l'etat courant.</summary>
    public IReadOnlyList<DeliveryStatus> AllowedTransitions { get; set; } = [];

    public Guid? CourierId { get; set; }
    public string? CourierName { get; set; }
    public string? CourierPhone { get; set; }
    public string? Notes { get; set; }
    public string? ReceivedBy { get; set; }
    public string? FailureReason { get; set; }
    public int AttemptCount { get; set; }
    public bool OrderNotified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? PickedUpAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public List<DeliveryLineResponse> Lines { get; set; } = [];
}

/// <summary>Ligne de bordereau envoyee par order-service.</summary>
public sealed class CreateDeliveryLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [MaxLength(40)]
    public string? Sku { get; set; }

    [MaxLength(160)]
    public string? ProductName { get; set; }

    [Range(1, 1_000_000)]
    public int Quantity { get; set; }
}

/// <summary>
/// Creation d'une livraison. Appele par order-service a l'expedition de la
/// commande. Idempotent : un second appel pour le meme numero de commande
/// renvoie le bordereau existant.
/// </summary>
public sealed class CreateDeliveryRequest
{
    [Required]
    public Guid OrderId { get; set; }

    [Required(AllowEmptyStrings = false), MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required]
    public Guid CustomerId { get; set; }

    [Required(AllowEmptyStrings = false), MaxLength(160)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? CustomerPhone { get; set; }

    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    [Required, MinLength(1, ErrorMessage = "Un bordereau de livraison doit contenir au moins une ligne.")]
    public List<CreateDeliveryLineRequest> Lines { get; set; } = [];
}

/// <summary>Affectation d'un livreur.</summary>
public sealed class AssignCourierRequest
{
    [Required]
    public Guid CourierId { get; set; }

    [MaxLength(300)]
    public string? Comment { get; set; }
}

/// <summary>
/// Mise a jour du statut d'une livraison par le livreur ou le back-office.
/// </summary>
public sealed class UpdateDeliveryStatusRequest
{
    /// <summary>Nouvel etat demande.</summary>
    [Required]
    public DeliveryStatus Status { get; set; }

    /// <summary>Position declaree (ex. « Carrefour Ndokoti »).</summary>
    [MaxLength(120)]
    public string? Location { get; set; }

    [MaxLength(300)]
    public string? Comment { get; set; }

    /// <summary>Nom du destinataire : obligatoire pour passer a <c>Livree</c>.</summary>
    [MaxLength(160)]
    public string? ReceivedBy { get; set; }

    /// <summary>Motif d'echec : obligatoire pour passer a <c>Echouee</c>.</summary>
    [MaxLength(300)]
    public string? FailureReason { get; set; }
}

/// <summary>Motif d'annulation.</summary>
public sealed class CancelDeliveryRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "reason est obligatoire pour annuler une livraison.")]
    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Filtres de recherche de livraisons.</summary>
public sealed class DeliveryQuery : PageQuery
{
    public DeliveryStatus? Status { get; set; }

    public Guid? CourierId { get; set; }

    /// <summary>Recherche libre sur la reference de livraison ou le numero de commande.</summary>
    [MaxLength(40)]
    public string? Search { get; set; }

    [MaxLength(80)]
    public string? City { get; set; }

    /// <summary>Ne renvoyer que les livraisons sans livreur affecte.</summary>
    public bool? UnassignedOnly { get; set; }

    public DateTimeOffset? From { get; set; }

    public DateTimeOffset? To { get; set; }

    /// <summary>Tri : <c>createdat</c> ou <c>reference</c>, suffixe <c>_desc</c> possible.</summary>
    [MaxLength(30)]
    public string? Sort { get; set; }
}
