using System.ComponentModel.DataAnnotations;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.Shared.Web;

namespace GestionCommandes.OrderService.Dtos;

// ================================= Clients =================================

/// <summary>Client expose par l'API.</summary>
public sealed class CustomerResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Code client.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Raison sociale ou nom.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    public string? Phone { get; set; }

    /// <summary>Ville.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>Adresse.</summary>
    public string? Address { get; set; }

    /// <summary>Client actif.</summary>
    public bool IsActive { get; set; }

    /// <summary>Nombre de commandes.</summary>
    public int OrderCount { get; set; }
}

/// <summary>Creation ou mise a jour d'un client.</summary>
public sealed class CustomerRequest
{
    /// <summary>Code client unique.</summary>
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Raison sociale ou nom.</summary>
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>Ville.</summary>
    [Required, MaxLength(80)]
    public string City { get; set; } = string.Empty;

    /// <summary>Adresse de livraison par defaut.</summary>
    [MaxLength(200)]
    public string? Address { get; set; }
}

/// <summary>Filtres de recherche de clients.</summary>
public sealed class CustomerQuery : PageQuery
{
    /// <summary>Recherche libre sur le code, le nom et l'email.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Filtre par etat d'activation.</summary>
    public bool? IsActive { get; set; }
}

// ================================ Commandes ================================

/// <summary>Ligne de commande exposee par l'API.</summary>
public sealed class OrderLineResponse
{
    /// <summary>Identifiant de la ligne.</summary>
    public long Id { get; set; }

    /// <summary>Produit commande.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article, renseignee a la confirmation.</summary>
    public string? Sku { get; set; }

    /// <summary>Libelle produit, renseigne a la confirmation.</summary>
    public string? ProductName { get; set; }

    /// <summary>Quantite.</summary>
    public int Quantity { get; set; }

    /// <summary>Prix unitaire fige. Vaut 0 en brouillon.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Total de la ligne.</summary>
    public decimal LineTotal { get; set; }
}

/// <summary>Etape de l'historique d'une commande.</summary>
public sealed class OrderHistoryResponse
{
    /// <summary>Etat de depart.</summary>
    public OrderStatus? FromStatus { get; set; }

    /// <summary>Nouvel etat.</summary>
    public OrderStatus ToStatus { get; set; }

    /// <summary>Motif ou commentaire.</summary>
    public string? Reason { get; set; }

    /// <summary>Auteur.</summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Horodatage.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}

/// <summary>Commande exposee par l'API.</summary>
public sealed class OrderResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Numero lisible.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Client.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Nom du client.</summary>
    public string? CustomerName { get; set; }

    /// <summary>Etat courant.</summary>
    public OrderStatus Status { get; set; }

    /// <summary>Etats atteignables depuis l'etat courant.</summary>
    public IReadOnlyList<string> AllowedTransitions { get; set; } = [];

    /// <summary>Adresse de livraison.</summary>
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    public string? ShippingCity { get; set; }

    /// <summary>Commentaire.</summary>
    public string? Notes { get; set; }

    /// <summary>Total hors taxes.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Taux de TVA.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Montant de TVA.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>Total toutes taxes comprises.</summary>
    public decimal Total { get; set; }

    /// <summary>Devise.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Motif du dernier echec de confirmation.</summary>
    public string? LastError { get; set; }

    /// <summary>Bordereau de livraison associe.</summary>
    public Guid? DeliveryId { get; set; }

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date de confirmation.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Date d'expedition.</summary>
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>Date de livraison.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Date d'annulation.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Motif d'annulation.</summary>
    public string? CancellationReason { get; set; }

    /// <summary>Lignes de la commande.</summary>
    public List<OrderLineResponse> Lines { get; set; } = [];

    /// <summary>Historique des etats.</summary>
    public List<OrderHistoryResponse> History { get; set; } = [];
}

/// <summary>Une ligne a la creation d'une commande.</summary>
public sealed class OrderLineRequest
{
    /// <summary>Produit commande.</summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Quantite.</summary>
    [Range(1, 100_000, ErrorMessage = "quantity doit etre au moins 1.")]
    public int Quantity { get; set; }
}

/// <summary>Creation d'une commande, en brouillon.</summary>
public sealed class CreateOrderRequest
{
    /// <summary>Client. Ignore pour un compte de role Client : c'est le sien.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Adresse de livraison. Par defaut, celle du client.</summary>
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison. Par defaut, celle du client.</summary>
    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    /// <summary>Commentaire.</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Lignes initiales, facultatives : on peut les ajouter ensuite.</summary>
    public List<OrderLineRequest> Lines { get; set; } = [];
}

/// <summary>Mise a jour des informations de livraison d'un brouillon.</summary>
public sealed class UpdateOrderRequest
{
    /// <summary>Adresse de livraison.</summary>
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    /// <summary>Commentaire.</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>Modification de la quantite d'une ligne.</summary>
public sealed class UpdateLineRequest
{
    /// <summary>Nouvelle quantite.</summary>
    [Range(1, 100_000, ErrorMessage = "quantity doit etre au moins 1.")]
    public int Quantity { get; set; }
}

/// <summary>Annulation d'une commande.</summary>
public sealed class CancelOrderRequest
{
    /// <summary>Motif, conserve dans l'historique.</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }
}

/// <summary>Filtres de recherche de commandes.</summary>
public sealed class OrderQuery : PageQuery
{
    /// <summary>Recherche libre sur le numero de commande.</summary>
    [MaxLength(60)]
    public string? Search { get; set; }

    /// <summary>Filtre par etat.</summary>
    public OrderStatus? Status { get; set; }

    /// <summary>Filtre par client.</summary>
    public Guid? CustomerId { get; set; }
}
