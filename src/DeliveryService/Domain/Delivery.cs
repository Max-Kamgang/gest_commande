using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.DeliveryService.Domain;

/// <summary>Etats d'une livraison.</summary>
public enum DeliveryStatus
{
    /// <summary>Bordereau cree, aucun livreur affecte.</summary>
    EnAttente = 1,

    /// <summary>Livreur affecte, enlevement a faire.</summary>
    Affectee = 2,

    /// <summary>Marchandise enlevee, livraison en cours de route.</summary>
    EnCours = 3,

    /// <summary>Livraison remise au client. Etat final.</summary>
    Livree = 4,

    /// <summary>Tentative echouee (client absent, adresse introuvable). Reaffectable.</summary>
    Echouee = 5,

    /// <summary>Livraison annulee. Etat final.</summary>
    Annulee = 6
}

/// <summary>Livreur.</summary>
public class Courier
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code livreur unique (ex. <c>LIV-001</c>).</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>Moyen de transport (camionnette, moto...).</summary>
    [MaxLength(40)]
    public string Vehicle { get; set; } = "Moto";

    /// <summary>Zone de tournee habituelle.</summary>
    [MaxLength(80)]
    public string? Zone { get; set; }

    /// <summary>Nombre maximum de livraisons simultanement en charge.</summary>
    public int MaxConcurrentDeliveries { get; set; } = 10;

    /// <summary>Un livreur inactif ne peut plus recevoir d'affectation.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Livraisons affectees.</summary>
    public List<Delivery> Deliveries { get; set; } = [];
}

/// <summary>
/// Bordereau de livraison, cree par order-service au passage de la commande
/// a l'etat Expediee. Le numero de commande est unique : la creation est donc
/// idempotente.
/// </summary>
public class Delivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference lisible unique : <c>LIV-2026-00001</c>.</summary>
    [MaxLength(30)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Commande d'origine (identifiant gere par order-service).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Numero de commande : cle d'idempotence de la creation.</summary>
    [MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    [MaxLength(160)]
    public string CustomerName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? CustomerPhone { get; set; }

    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    public DeliveryStatus Status { get; set; } = DeliveryStatus.EnAttente;

    public Guid? CourierId { get; set; }

    public Courier? Courier { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    /// <summary>Nom de la personne ayant receptionne la marchandise.</summary>
    [MaxLength(160)]
    public string? ReceivedBy { get; set; }

    /// <summary>Motif du dernier echec de livraison.</summary>
    [MaxLength(300)]
    public string? FailureReason { get; set; }

    /// <summary>Nombre de tentatives de livraison infructueuses.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Vrai une fois order-service averti de la livraison effective.</summary>
    public bool OrderNotified { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? AssignedAt { get; set; }

    public DateTimeOffset? PickedUpAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Verrou optimiste.</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    /// <summary>Contenu du bordereau.</summary>
    public List<DeliveryLine> Lines { get; set; } = [];

    /// <summary>Historique de suivi.</summary>
    public List<DeliveryTrackingEntry> Tracking { get; set; } = [];

    /// <summary>Ajoute une etape au suivi.</summary>
    public void AppendTracking(
        DeliveryStatus? from,
        DeliveryStatus to,
        string changedBy,
        string? comment = null,
        string? location = null)
        => Tracking.Add(new DeliveryTrackingEntry
        {
            DeliveryId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedBy = changedBy,
            Comment = comment,
            Location = location,
            ChangedAt = DateTimeOffset.UtcNow
        });
}

/// <summary>Ligne du bordereau de livraison.</summary>
public class DeliveryLine
{
    public long Id { get; set; }

    public Guid DeliveryId { get; set; }

    public Delivery? Delivery { get; set; }

    public Guid ProductId { get; set; }

    [MaxLength(40)]
    public string? Sku { get; set; }

    [MaxLength(160)]
    public string? ProductName { get; set; }

    public int Quantity { get; set; }
}

/// <summary>Etape de suivi d'une livraison.</summary>
public class DeliveryTrackingEntry
{
    public long Id { get; set; }

    public Guid DeliveryId { get; set; }

    public Delivery? Delivery { get; set; }

    public DeliveryStatus? FromStatus { get; set; }

    public DeliveryStatus ToStatus { get; set; }

    /// <summary>Lieu declare par le livreur.</summary>
    [MaxLength(120)]
    public string? Location { get; set; }

    [MaxLength(300)]
    public string? Comment { get; set; }

    [MaxLength(160)]
    public string ChangedBy { get; set; } = "systeme";

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Compteur de numerotation annuelle des livraisons.</summary>
public class DeliverySequence
{
    public int Year { get; set; }

    public int LastValue { get; set; }
}
