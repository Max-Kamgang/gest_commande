using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.OrderService.Domain;

namespace GestionCommandes.OrderService.Dtos;

// ------------------------------- Clients -------------------------------

/// <summary>Client expose par l'API.</summary>
public sealed class CustomerResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string City { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public int OrderCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation / mise a jour d'un client.</summary>
public sealed class CustomerRequest
{
    [Required, MaxLength(20), RegularExpression("^[A-Z0-9-]{3,20}$",
        ErrorMessage = "code doit etre en majuscules (lettres, chiffres, tiret), 3 a 20 caracteres.")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [Phone, MaxLength(30)]
    public string? Phone { get; set; }

    [Required, MaxLength(80)]
    public string City { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Address { get; set; }
}

/// <summary>Filtres de recherche de clients.</summary>
public sealed class CustomerQuery : PageQuery
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

// ------------------------------ Commandes ------------------------------

/// <summary>Ligne de commande exposee par l'API.</summary>
public sealed class OrderLineResponse
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public DateTimeOffset? PricedAt { get; set; }
}

/// <summary>Entree d'historique d'etat exposee par l'API.</summary>
public sealed class OrderHistoryResponse
{
    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }
    public string? Reason { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>Commande exposee par l'API.</summary>
public sealed class OrderResponse
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerCode { get; set; }
    public OrderStatus Status { get; set; }

    /// <summary>Transitions possibles depuis l'etat courant (pilote l'interface web).</summary>
    public IReadOnlyList<OrderStatus> AllowedTransitions { get; set; } = [];

    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public string? Notes { get; set; }
    public decimal SubTotal { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "XAF";

    /// <summary>Motif de la derniere confirmation echouee (compensation de saga).</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? LastErrorAt { get; set; }
    public Guid? DeliveryId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? PreparedAt { get; set; }
    public DateTimeOffset? ShippedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public List<OrderLineResponse> Lines { get; set; } = [];
}

/// <summary>Ligne demandee a la creation d'une commande.</summary>
public sealed class CreateOrderLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, 100_000, ErrorMessage = "quantity doit etre un entier strictement positif.")]
    public int Quantity { get; set; }
}

/// <summary>Creation d'une commande (toujours creee en Brouillon).</summary>
public sealed class CreateOrderRequest
{
    /// <summary>
    /// Client concerne. Facultatif pour un compte de role Client : son propre
    /// identifiant est alors deduit du jeton.
    /// </summary>
    public Guid? CustomerId { get; set; }

    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Lignes initiales (facultatives : une commande peut etre creee vide).</summary>
    [MaxLength(200, ErrorMessage = "200 lignes maximum par commande.")]
    public List<CreateOrderLineRequest> Lines { get; set; } = [];
}

/// <summary>Mise a jour des informations d'expedition d'un brouillon.</summary>
public sealed class UpdateOrderRequest
{
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>Ajout ou modification d'une ligne de brouillon.</summary>
public sealed class OrderLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, 100_000, ErrorMessage = "quantity doit etre un entier strictement positif.")]
    public int Quantity { get; set; }
}

/// <summary>Modification de la quantite d'une ligne.</summary>
public sealed class UpdateOrderLineRequest
{
    [Range(1, 100_000, ErrorMessage = "quantity doit etre un entier strictement positif.")]
    public int Quantity { get; set; }
}

/// <summary>Motif d'annulation.</summary>
public sealed class CancelOrderRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "reason est obligatoire pour annuler une commande.")]
    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Filtres de recherche de commandes.</summary>
public sealed class OrderQuery : PageQuery
{
    /// <summary>Recherche libre sur le numero de commande.</summary>
    [MaxLength(40)]
    public string? Number { get; set; }

    public Guid? CustomerId { get; set; }

    public OrderStatus? Status { get; set; }

    /// <summary>Debut de periode de creation (inclus, UTC).</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Fin de periode de creation (incluse, UTC).</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Tri : <c>createdat</c>, <c>number</c> ou <c>total</c>, suffixe <c>_desc</c> possible.</summary>
    [MaxLength(30)]
    public string? Sort { get; set; }
}

/// <summary>Notification de livraison envoyee par delivery-service.</summary>
public sealed class DeliveryCompletedNotification
{
    /// <summary>Identifiant de la livraison dans delivery-service.</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>Date effective de livraison.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Nom du destinataire ayant signe.</summary>
    [MaxLength(160)]
    public string? ReceivedBy { get; set; }
}
