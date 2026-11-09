using System.ComponentModel.DataAnnotations;
using GestionCommandes.CatalogService.Domain;
using GestionCommandes.Shared.Web;

namespace GestionCommandes.CatalogService.Dtos;

// =============================== Categories ===============================

/// <summary>Categorie exposee par l'API.</summary>
public sealed class CategoryResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Code court.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Categorie active.</summary>
    public bool IsActive { get; set; }

    /// <summary>Nombre de produits rattaches.</summary>
    public int ProductCount { get; set; }
}

/// <summary>Creation ou mise a jour d'une categorie.</summary>
public sealed class CategoryRequest
{
    /// <summary>Code court en majuscules.</summary>
    [Required, MaxLength(30)]
    [RegularExpression("^[A-Z0-9_-]{2,30}$",
        ErrorMessage = "code doit etre en majuscules (lettres, chiffres, - et _), 2 a 30 caracteres.")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;
}

// ================================ Produits ================================

/// <summary>Produit expose par l'API, avec son stock.</summary>
public sealed class ProductResponse
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; }

    /// <summary>Reference article.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; set; }

    /// <summary>Categorie.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Libelle de la categorie.</summary>
    public string? CategoryName { get; set; }

    /// <summary>Prix unitaire courant.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Devise.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Unite de vente.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Produit commandable.</summary>
    public bool IsActive { get; set; }

    /// <summary>Quantite physique.</summary>
    public int QuantityOnHand { get; set; }

    /// <summary>Quantite reservee.</summary>
    public int QuantityReserved { get; set; }

    /// <summary>Quantite vendable.</summary>
    public int QuantityAvailable { get; set; }

    /// <summary>Seuil d'alerte.</summary>
    public int LowStockThreshold { get; set; }

    /// <summary>Vrai si le disponible est sous le seuil.</summary>
    public bool IsLowStock { get; set; }

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date de derniere modification.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation d'un produit.</summary>
public sealed class CreateProductRequest
{
    /// <summary>Reference article unique.</summary>
    [Required, MaxLength(40)]
    [RegularExpression("^[A-Za-z0-9._-]{3,40}$",
        ErrorMessage = "sku doit contenir 3 a 40 caracteres alphanumeriques, point, tiret ou underscore.")]
    public string Sku { get; set; } = string.Empty;

    /// <summary>Libelle.</summary>
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Categorie de rattachement.</summary>
    [Required]
    public Guid CategoryId { get; set; }

    /// <summary>Prix unitaire, strictement positif.</summary>
    [Range(0.01, 1_000_000_000, ErrorMessage = "unitPrice doit etre strictement positif.")]
    public decimal UnitPrice { get; set; }

    /// <summary>Unite de vente. « piece » si elle n'est pas precisee.</summary>
    [MaxLength(20)]
    public string? Unit { get; set; }

    /// <summary>Stock initial.</summary>
    [Range(0, 1_000_000)]
    public int QuantityOnHand { get; set; }

    /// <summary>Seuil d'alerte de stock bas.</summary>
    [Range(0, 1_000_000)]
    public int LowStockThreshold { get; set; } = 5;
}

/// <summary>Mise a jour d'un produit, hors prix et stock.</summary>
public sealed class UpdateProductRequest
{
    /// <summary>Libelle.</summary>
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Description.</summary>
    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>Categorie de rattachement.</summary>
    [Required]
    public Guid CategoryId { get; set; }

    /// <summary>Unite de vente. Inchangee si elle n'est pas precisee.</summary>
    [MaxLength(20)]
    public string? Unit { get; set; }

    /// <summary>Seuil d'alerte de stock bas.</summary>
    [Range(0, 1_000_000)]
    public int LowStockThreshold { get; set; } = 5;
}

/// <summary>Revalorisation d'un produit.</summary>
public sealed class ChangePriceRequest
{
    /// <summary>Nouveau prix unitaire.</summary>
    [Range(0.01, 1_000_000_000, ErrorMessage = "unitPrice doit etre strictement positif.")]
    public decimal UnitPrice { get; set; }
}

/// <summary>Activation ou desactivation.</summary>
public sealed class SetActiveRequest
{
    /// <summary>Nouvel etat.</summary>
    [Required]
    public bool IsActive { get; set; }
}

/// <summary>Filtres de recherche de produits.</summary>
public sealed class ProductQuery : PageQuery
{
    /// <summary>Recherche libre sur la reference, le nom et la description.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Filtre par categorie.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Filtre par etat d'activation.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Ne garder que les produits sous leur seuil d'alerte.</summary>
    public bool? LowStockOnly { get; set; }

    /// <summary>Tri : <c>name</c>, <c>price</c>, <c>sku</c> ou <c>available</c>, suffixe <c>_desc</c> possible.</summary>
    [MaxLength(30)]
    public string? Sort { get; set; }
}

// ================================= Stock =================================

/// <summary>Entree en stock : reception fournisseur ou retour client.</summary>
public sealed class StockEntryRequest
{
    /// <summary>Produit concerne.</summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Quantite recue, strictement positive.</summary>
    [Range(1, 1_000_000, ErrorMessage = "quantity doit etre au moins 1.")]
    public int Quantity { get; set; }

    /// <summary>Motif ou numero de bon de reception.</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }
}

/// <summary>Correction d'inventaire : la quantite physique est redefinie.</summary>
public sealed class StockAdjustmentRequest
{
    /// <summary>Produit concerne.</summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Quantite physique constatee.</summary>
    [Range(0, 1_000_000)]
    public int QuantityOnHand { get; set; }

    /// <summary>Motif de l'ajustement, obligatoire pour la tracabilite.</summary>
    [Required(ErrorMessage = "reason est obligatoire pour un ajustement."), MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Mouvement de stock expose par l'API.</summary>
public sealed class MovementResponse
{
    /// <summary>Identifiant.</summary>
    public long Id { get; set; }

    /// <summary>Produit concerne.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article.</summary>
    public string? Sku { get; set; }

    /// <summary>Libelle du produit.</summary>
    public string? ProductName { get; set; }

    /// <summary>Nature du mouvement.</summary>
    public MovementType Type { get; set; }

    /// <summary>Quantite.</summary>
    public int Quantity { get; set; }

    /// <summary>Quantite physique apres mouvement.</summary>
    public int QuantityOnHandAfter { get; set; }

    /// <summary>Quantite reservee apres mouvement.</summary>
    public int QuantityReservedAfter { get; set; }

    /// <summary>Reference metier.</summary>
    public string? Reference { get; set; }

    /// <summary>Motif.</summary>
    public string? Reason { get; set; }

    /// <summary>Auteur.</summary>
    public string PerformedBy { get; set; } = string.Empty;

    /// <summary>Horodatage.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Filtres de recherche de mouvements.</summary>
public sealed class MovementQuery : PageQuery
{
    /// <summary>Filtre par produit.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>Filtre par nature de mouvement.</summary>
    public MovementType? Type { get; set; }

    /// <summary>Filtre par reference metier (numero de commande).</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }
}

// ============================= Reservations =============================

/// <summary>Une ligne a reserver.</summary>
public sealed class ReservationLineRequest
{
    /// <summary>Produit a reserver.</summary>
    [Required]
    public Guid ProductId { get; set; }

    /// <summary>Quantite demandee.</summary>
    [Range(1, 1_000_000, ErrorMessage = "quantity doit etre au moins 1.")]
    public int Quantity { get; set; }
}

/// <summary>
/// Demande de reservation emise par order-service a la confirmation d'une commande.
///
/// Un seul appel fait les deux choses dont la commande a besoin : il verifie et
/// immobilise le stock, ET renvoie les prix du catalogue a figer dans les lignes.
/// </summary>
public sealed class ReserveStockRequest
{
    /// <summary>Reference metier : le numero de commande. Unique.</summary>
    [Required, MaxLength(60)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Lignes a reserver.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins une ligne est requise.")]
    public List<ReservationLineRequest> Lines { get; set; } = [];
}

/// <summary>Ligne reservee, avec le prix catalogue a figer.</summary>
public sealed class ReservedLineResponse
{
    /// <summary>Produit reserve.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>Libelle du produit.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Quantite reservee.</summary>
    public int Quantity { get; set; }

    /// <summary>Prix unitaire au moment de la reservation.</summary>
    public decimal UnitPrice { get; set; }
}

/// <summary>Reponse a une demande de reservation.</summary>
public sealed class ReservationResponse
{
    /// <summary>Identifiant de la reservation.</summary>
    public Guid Id { get; set; }

    /// <summary>Reference metier.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Etat courant.</summary>
    public ReservationStatus Status { get; set; }

    /// <summary>Devise des prix renvoyes.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Lignes reservees et tarifees.</summary>
    public List<ReservedLineResponse> Lines { get; set; } = [];

    /// <summary>Date de pose.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Date de liberation ou de consommation.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }
}
