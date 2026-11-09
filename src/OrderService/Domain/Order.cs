using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.OrderService.Domain;

/// <summary>Client de la plateforme.</summary>
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code client unique (ex. <c>CLI-001</c>).</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(80)]
    public string City { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Address { get; set; }

    /// <summary>Un client desactive ne peut plus passer de nouvelle commande.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Commandes du client.</summary>
    public List<Order> Orders { get; set; } = [];
}

/// <summary>Commande client.</summary>
public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Numero lisible, unique : <c>CMD-2026-00001</c>. Sert de reference a la saga.</summary>
    [MaxLength(30)]
    public string Number { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Brouillon;

    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Total hors taxes (somme des lignes).</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Taux de TVA applique, fige a la confirmation.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Montant de TVA.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>Total toutes taxes comprises.</summary>
    public decimal Total { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "XAF";

    /// <summary>
    /// Message de la derniere tentative de confirmation echouee.
    /// La commande reste en Brouillon et le motif est consultable par le client :
    /// c'est la trace de la compensation de saga.
    /// </summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>Date de la derniere tentative de confirmation echouee.</summary>
    public DateTimeOffset? LastErrorAt { get; set; }

    /// <summary>Identifiant de la livraison creee dans delivery-service.</summary>
    public Guid? DeliveryId { get; set; }

    [MaxLength(160)]
    public string CreatedBy { get; set; } = "systeme";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ConfirmedAt { get; set; }

    public DateTimeOffset? PreparedAt { get; set; }

    public DateTimeOffset? ShippedAt { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    [MaxLength(300)]
    public string? CancellationReason { get; set; }

    /// <summary>Verrou optimiste : empeche deux transitions concurrentes sur la meme commande.</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    /// <summary>Lignes de la commande.</summary>
    public List<OrderLine> Lines { get; set; } = [];

    /// <summary>Historique des changements d'etat.</summary>
    public List<OrderStatusHistoryEntry> History { get; set; } = [];

    /// <summary>Recalcule les totaux a partir des lignes.</summary>
    public void RecomputeTotals(decimal vatRate)
    {
        SubTotal = Lines.Sum(l => l.LineTotal);
        VatRate = vatRate;
        VatAmount = Math.Round(SubTotal * vatRate, 2, MidpointRounding.AwayFromZero);
        Total = SubTotal + VatAmount;
    }

    /// <summary>Ajoute une entree a l'historique d'etats.</summary>
    public void AppendHistory(OrderStatus? from, OrderStatus to, string changedBy, string? reason = null)
    {
        History.Add(new OrderStatusHistoryEntry
        {
            OrderId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedBy = changedBy,
            Reason = reason,
            ChangedAt = DateTimeOffset.UtcNow
        });
    }
}

/// <summary>
/// Ligne de commande. Le prix unitaire y est <b>fige</b> a la confirmation :
/// une revalorisation du catalogue ne modifie jamais une commande deja confirmee.
/// </summary>
public class OrderLine
{
    public long Id { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>Produit commande (identifiant gere par catalog-service).</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article, recopiee du catalogue a la confirmation.</summary>
    [MaxLength(40)]
    public string? Sku { get; set; }

    /// <summary>Libelle produit, recopie du catalogue a la confirmation.</summary>
    [MaxLength(160)]
    public string? ProductName { get; set; }

    public int Quantity { get; set; }

    /// <summary>Prix unitaire fige. Vaut 0 tant que la commande est en brouillon.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Total de la ligne (<see cref="Quantity"/> x <see cref="UnitPrice"/>).</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Date a laquelle le prix a ete fige.</summary>
    public DateTimeOffset? PricedAt { get; set; }

    /// <summary>Applique un prix catalogue a la ligne et recalcule son total.</summary>
    public void ApplyPrice(decimal unitPrice, string sku, string productName)
    {
        UnitPrice = unitPrice;
        Sku = sku;
        ProductName = productName;
        LineTotal = Math.Round(unitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
        PricedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>Entree d'historique d'etat.</summary>
public class OrderStatusHistoryEntry
{
    public long Id { get; set; }

    public Guid OrderId { get; set; }

    public Order? Order { get; set; }

    /// <summary>Etat de depart (nul a la creation).</summary>
    public OrderStatus? FromStatus { get; set; }

    public OrderStatus ToStatus { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    [MaxLength(160)]
    public string ChangedBy { get; set; } = "systeme";

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Compteur de numerotation annuelle des commandes.</summary>
public class OrderSequence
{
    /// <summary>Annee civile.</summary>
    public int Year { get; set; }

    /// <summary>Dernier numero attribue pour cette annee.</summary>
    public int LastValue { get; set; }
}
