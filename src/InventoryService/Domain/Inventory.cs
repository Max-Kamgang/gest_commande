using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.InventoryService.Domain;

/// <summary>Entrepot physique.</summary>
public class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code court unique (ex. <c>DLA-01</c>).</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string City { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Address { get; set; }

    /// <summary>Un entrepot desactive n'est plus eligible aux reservations.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Lignes de stock de cet entrepot.</summary>
    public List<StockItem> StockItems { get; set; } = [];
}

/// <summary>
/// Stock d'un produit dans un entrepot.
///
/// Le stock se lit sur trois chiffres :
/// <list type="bullet">
///   <item><see cref="QuantityOnHand"/> : quantite physiquement presente,</item>
///   <item><see cref="QuantityReserved"/> : part deja promise a des commandes confirmees,</item>
///   <item><see cref="QuantityAvailable"/> : ce qui reste reellement vendable.</item>
/// </list>
/// </summary>
public class StockItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Produit concerne (identifiant gere par catalog-service).</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Libelle d'affichage du produit, recopie du catalogue par l'appelant
    /// (entree de stock ou reservation). inventory-service ne connait pas le
    /// catalogue : cette denormalisation sert uniquement a rendre les alertes
    /// lisibles, sans introduire d'appel bloquant vers catalog-service sur le
    /// chemin critique du stock.
    /// </summary>
    [MaxLength(180)]
    public string? ProductLabel { get; set; }

    public Guid WarehouseId { get; set; }

    public Warehouse? Warehouse { get; set; }

    /// <summary>Quantite physiquement en stock.</summary>
    public int QuantityOnHand { get; set; }

    /// <summary>Quantite reservee par des commandes confirmees mais pas encore expediees.</summary>
    public int QuantityReserved { get; set; }

    /// <summary>Quantite disponible a la vente.</summary>
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>Seuil en dessous duquel une alerte « stock bas » est emise.</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>Vrai si le disponible est au niveau du seuil ou en dessous.</summary>
    public bool IsLowStock => QuantityAvailable <= LowStockThreshold;

    /// <summary>
    /// Jeton de concurrence optimiste. SQLite n'a pas de type <c>rowversion</c> :
    /// la valeur est un GUID regenere a chaque enregistrement par le DbContext.
    /// Deux reservations simultanees sur la meme ligne : la seconde echoue avec
    /// <c>DbUpdateConcurrencyException</c>, ce qui empeche la survente.
    /// </summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Nature d'un mouvement de stock.</summary>
public enum MovementType
{
    /// <summary>Entree en stock (reception fournisseur, retour).</summary>
    Entree = 1,

    /// <summary>Sortie definitive hors commande (casse, perte, consommation interne).</summary>
    Sortie = 2,

    /// <summary>Correction d'inventaire : la quantite physique est redefinie.</summary>
    Ajustement = 3,

    /// <summary>Reservation posee par la confirmation d'une commande.</summary>
    Reservation = 4,

    /// <summary>Liberation d'une reservation (annulation ou compensation de saga).</summary>
    LiberationReservation = 5,

    /// <summary>Sortie definitive consommant une reservation (expedition de la commande).</summary>
    SortieSurReservation = 6
}

/// <summary>Trace immuable d'un mouvement de stock.</summary>
public class StockMovement
{
    public long Id { get; set; }

    public Guid StockItemId { get; set; }

    public StockItem? StockItem { get; set; }

    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    public MovementType Type { get; set; }

    /// <summary>Quantite du mouvement (toujours positive : le sens est porte par <see cref="Type"/>).</summary>
    public int Quantity { get; set; }

    /// <summary>Quantite physique apres le mouvement.</summary>
    public int QuantityOnHandAfter { get; set; }

    /// <summary>Quantite reservee apres le mouvement.</summary>
    public int QuantityReservedAfter { get; set; }

    /// <summary>Reference metier (numero de commande, bon de reception...).</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    [MaxLength(160)]
    public string PerformedBy { get; set; } = "systeme";

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Etat d'une reservation de stock.</summary>
public enum ReservationStatus
{
    /// <summary>Stock immobilise, en attente d'expedition.</summary>
    Active = 1,

    /// <summary>Reservation annulee : le stock est rendu disponible (compensation).</summary>
    Liberee = 2,

    /// <summary>Reservation transformee en sortie definitive (commande expediee).</summary>
    Consommee = 3
}

/// <summary>
/// Reservation de stock rattachee a une reference metier (le numero de commande).
/// C'est l'unite de compensation de la saga : la liberer suffit a annuler l'etape
/// de reservation, quel que soit le nombre de lignes concernees.
/// </summary>
public class StockReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference metier unique (numero de commande).</summary>
    [MaxLength(60)]
    public string Reference { get; set; } = string.Empty;

    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

    [MaxLength(160)]
    public string CreatedBy { get; set; } = "systeme";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de liberation ou de consommation.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Lignes reservees (un produit peut etre pris sur plusieurs entrepots).</summary>
    public List<StockReservationLine> Lines { get; set; } = [];
}

/// <summary>Ligne d'une reservation : une quantite d'un produit dans un entrepot donne.</summary>
public class StockReservationLine
{
    public long Id { get; set; }

    public Guid ReservationId { get; set; }

    public StockReservation? Reservation { get; set; }

    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    public int Quantity { get; set; }
}
