using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.OrderService.Domain;

/// <summary>Client de la plateforme.</summary>
public class Customer
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code client unique, par exemple <c>CLI-001</c>.</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Raison sociale ou nom.</summary>
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    [MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>Ville.</summary>
    [MaxLength(80)]
    public string City { get; set; } = string.Empty;

    /// <summary>Adresse de livraison par defaut.</summary>
    [MaxLength(200)]
    public string? Address { get; set; }

    /// <summary>Un client desactive ne peut plus passer de commande.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Commandes du client.</summary>
    public List<Order> Orders { get; set; } = [];
}

/// <summary>Commande client.</summary>
public class Order
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Numero lisible et unique (<c>CMD-2026-00001</c>). Il sert aussi de reference
    /// de reservation chez catalog-service et de reference de bordereau chez
    /// delivery-service : un seul identifiant suit la commande partout.
    /// </summary>
    [MaxLength(30)]
    public string Number { get; set; } = string.Empty;

    /// <summary>Client.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Navigation vers le client.</summary>
    public Customer? Customer { get; set; }

    /// <summary>Etat courant.</summary>
    public OrderStatus Status { get; set; } = OrderStatus.Brouillon;

    /// <summary>Adresse de livraison.</summary>
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    /// <summary>Commentaire libre.</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    /// <summary>Total hors taxes.</summary>
    public decimal SubTotal { get; set; }

    /// <summary>Taux de TVA applique, fige a la confirmation.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Montant de TVA.</summary>
    public decimal VatAmount { get; set; }

    /// <summary>Total toutes taxes comprises.</summary>
    public decimal Total { get; set; }

    /// <summary>Devise.</summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "XAF";

    /// <summary>
    /// Motif de la derniere tentative de confirmation echouee. La commande reste
    /// en brouillon et le client peut lire pourquoi : c'est la trace visible de
    /// l'annulation des etapes deja faites.
    /// </summary>
    [MaxLength(1000)]
    public string? LastError { get; set; }

    /// <summary>Identifiant du bordereau cree dans delivery-service.</summary>
    public Guid? DeliveryId { get; set; }

    /// <summary>Auteur de la creation.</summary>
    [MaxLength(160)]
    public string CreatedBy { get; set; } = "systeme";

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de confirmation.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }

    /// <summary>Date d'expedition.</summary>
    public DateTimeOffset? ShippedAt { get; set; }

    /// <summary>Date de livraison.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Date d'annulation.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Motif d'annulation.</summary>
    [MaxLength(300)]
    public string? CancellationReason { get; set; }

    /// <summary>Verrou optimiste : empeche deux transitions simultanees.</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    /// <summary>Lignes de la commande.</summary>
    public List<OrderLine> Lines { get; set; } = [];

    /// <summary>Historique des changements d'etat.</summary>
    public List<OrderHistoryEntry> History { get; set; } = [];

    /// <summary>Recalcule les totaux a partir des lignes.</summary>
    /// <param name="vatRate">Taux de TVA a appliquer.</param>
    public void RecomputeTotals(decimal vatRate)
    {
        SubTotal = Lines.Sum(line => line.LineTotal);
        VatRate = vatRate;
        VatAmount = Math.Round(SubTotal * vatRate, 2, MidpointRounding.AwayFromZero);
        Total = SubTotal + VatAmount;
    }

    /// <summary>Ajoute une entree a l'historique d'etats.</summary>
    /// <param name="from">Etat de depart, nul a la creation.</param>
    /// <param name="to">Nouvel etat.</param>
    /// <param name="changedBy">Auteur.</param>
    /// <param name="reason">Motif ou commentaire.</param>
    public void AppendHistory(OrderStatus? from, OrderStatus to, string changedBy, string? reason = null)
        => History.Add(new OrderHistoryEntry
        {
            OrderId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedBy = changedBy,
            Reason = reason
        });
}

/// <summary>
/// Ligne de commande. Le prix unitaire y est FIGE a la confirmation : une
/// revalorisation du catalogue ne change jamais une commande deja confirmee.
/// </summary>
public class OrderLine
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Commande de rattachement.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation vers la commande.</summary>
    public Order? Order { get; set; }

    /// <summary>Produit commande (identifiant gere par catalog-service).</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article, recopiee du catalogue a la confirmation.</summary>
    [MaxLength(40)]
    public string? Sku { get; set; }

    /// <summary>Libelle produit, recopie du catalogue a la confirmation.</summary>
    [MaxLength(160)]
    public string? ProductName { get; set; }

    /// <summary>Quantite commandee.</summary>
    public int Quantity { get; set; }

    /// <summary>Prix unitaire fige. Vaut 0 tant que la commande est en brouillon.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Total de la ligne.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Applique un prix catalogue a la ligne et recalcule son total.</summary>
    /// <param name="unitPrice">Prix unitaire renvoye par le catalogue.</param>
    /// <param name="sku">Reference article.</param>
    /// <param name="productName">Libelle du produit.</param>
    public void ApplyPrice(decimal unitPrice, string sku, string productName)
    {
        UnitPrice = unitPrice;
        Sku = sku;
        ProductName = productName;
        LineTotal = Math.Round(unitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Entree d'historique d'etat.</summary>
public class OrderHistoryEntry
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Commande concernee.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation vers la commande.</summary>
    public Order? Order { get; set; }

    /// <summary>Etat de depart, nul a la creation.</summary>
    public OrderStatus? FromStatus { get; set; }

    /// <summary>Nouvel etat.</summary>
    public OrderStatus ToStatus { get; set; }

    /// <summary>Motif ou commentaire.</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Auteur du changement.</summary>
    [MaxLength(160)]
    public string ChangedBy { get; set; } = "systeme";

    /// <summary>Horodatage.</summary>
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Compteur de numerotation annuelle des commandes.</summary>
public class OrderSequence
{
    /// <summary>Annee civile, cle de la table.</summary>
    public int Year { get; set; }

    /// <summary>Dernier numero attribue pour cette annee.</summary>
    public int LastValue { get; set; }
}
