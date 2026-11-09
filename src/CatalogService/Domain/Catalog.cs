using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCommandes.CatalogService.Domain;

/// <summary>Categorie de produits.</summary>
public class Category
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code court unique, par exemple <c>RESEAU</c>.</summary>
    [MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Une categorie desactivee masque ses produits du catalogue.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Produits rattaches.</summary>
    public List<Product> Products { get; set; } = [];
}

/// <summary>
/// Produit vendable, avec son stock.
///
/// Choix de conception : la fiche produit et le stock sont la MEME entite. Ce
/// service est le seul proprietaire des deux, et cela evite une jointure et un
/// aller-retour sur le chemin critique de la commande. Le stock se lit sur trois
/// chiffres : <see cref="QuantityOnHand"/> (present en magasin),
/// <see cref="QuantityReserved"/> (promis a des commandes confirmees) et
/// <see cref="QuantityAvailable"/> (reellement vendable).
/// </summary>
public class Product
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference article unique.</summary>
    [MaxLength(40)]
    public string Sku { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description commerciale.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Categorie de rattachement.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Navigation vers la categorie.</summary>
    public Category? Category { get; set; }

    /// <summary>
    /// Prix unitaire courant. C'est ce prix que order-service recupere a la
    /// confirmation et FIGE dans la ligne de commande : une hausse ulterieure
    /// ne modifie pas les commandes deja confirmees.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Unite de vente (piece, carton...).</summary>
    [MaxLength(20)]
    public string Unit { get; set; } = "piece";

    /// <summary>Un produit desactive ne peut plus etre commande.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Quantite physiquement en stock.</summary>
    public int QuantityOnHand { get; set; }

    /// <summary>Quantite reservee par des commandes confirmees mais pas encore expediees.</summary>
    public int QuantityReserved { get; set; }

    /// <summary>Seuil en dessous duquel le produit remonte dans les alertes.</summary>
    public int LowStockThreshold { get; set; } = 5;

    /// <summary>
    /// Jeton de concurrence. SQLite n'a pas de type <c>rowversion</c> : on utilise
    /// un GUID regenere a chaque enregistrement. Deux reservations simultanees sur
    /// le meme produit : la seconde echoue proprement au lieu de survendre.
    /// </summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de derniere modification.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Quantite reellement vendable.</summary>
    [NotMapped]
    public int QuantityAvailable => QuantityOnHand - QuantityReserved;

    /// <summary>Vrai si le disponible est au niveau du seuil ou en dessous.</summary>
    [NotMapped]
    public bool IsLowStock => QuantityAvailable <= LowStockThreshold;
}

/// <summary>Nature d'un mouvement de stock.</summary>
public enum MovementType
{
    /// <summary>Entree en stock : reception fournisseur ou retour client.</summary>
    Entree = 1,

    /// <summary>Correction d'inventaire : la quantite physique est redefinie.</summary>
    Ajustement = 2,

    /// <summary>Reservation posee par la confirmation d'une commande.</summary>
    Reservation = 3,

    /// <summary>Liberation d'une reservation : annulation de commande.</summary>
    Liberation = 4,

    /// <summary>Sortie definitive consommant une reservation : expedition.</summary>
    Sortie = 5
}

/// <summary>
/// Trace d'un mouvement de stock. Table en ajout seul : c'est l'historique qui
/// explique comment on est arrive au stock courant.
/// </summary>
public class StockMovement
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Produit concerne.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Navigation vers le produit.</summary>
    public Product? Product { get; set; }

    /// <summary>Nature du mouvement.</summary>
    public MovementType Type { get; set; }

    /// <summary>Quantite, toujours positive : le sens est porte par <see cref="Type"/>.</summary>
    public int Quantity { get; set; }

    /// <summary>Quantite physique apres le mouvement.</summary>
    public int QuantityOnHandAfter { get; set; }

    /// <summary>Quantite reservee apres le mouvement.</summary>
    public int QuantityReservedAfter { get; set; }

    /// <summary>Reference metier : numero de commande, bon de reception...</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    /// <summary>Motif saisi par l'operateur.</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Auteur du mouvement.</summary>
    [MaxLength(160)]
    public string PerformedBy { get; set; } = "systeme";

    /// <summary>Horodatage.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Etat d'une reservation de stock.</summary>
public enum ReservationStatus
{
    /// <summary>Stock immobilise, en attente d'expedition.</summary>
    Active = 1,

    /// <summary>Reservation annulee : le stock est redevenu disponible.</summary>
    Liberee = 2,

    /// <summary>Reservation transformee en sortie definitive.</summary>
    Consommee = 3
}

/// <summary>
/// Reservation de stock rattachee a une reference metier : le numero de commande.
///
/// C'est l'unite d'annulation. Si la confirmation d'une commande echoue apres la
/// reservation, order-service appelle « liberer » avec ce numero et tout est rendu
/// au stock, quel que soit le nombre de lignes.
/// </summary>
public class StockReservation
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference metier unique : le numero de commande.</summary>
    [MaxLength(60)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Etat courant.</summary>
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;

    /// <summary>Auteur de la reservation.</summary>
    [MaxLength(160)]
    public string CreatedBy { get; set; } = "systeme";

    /// <summary>Date de pose.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de liberation ou de consommation.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Lignes reservees.</summary>
    public List<StockReservationLine> Lines { get; set; } = [];
}

/// <summary>Ligne de reservation : une quantite d'un produit.</summary>
public class StockReservationLine
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Reservation de rattachement.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>Navigation vers la reservation.</summary>
    public StockReservation? Reservation { get; set; }

    /// <summary>Produit reserve.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Quantite reservee.</summary>
    public int Quantity { get; set; }

    /// <summary>Prix unitaire au moment de la reservation, renvoye a order-service.</summary>
    public decimal UnitPrice { get; set; }
}
