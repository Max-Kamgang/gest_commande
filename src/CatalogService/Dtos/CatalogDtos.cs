using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;

namespace GestionCommandes.CatalogService.Dtos;

// ----------------------------- Categories -----------------------------

/// <summary>Categorie exposee par l'API.</summary>
public sealed class CategoryResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int ProductCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation / mise a jour d'une categorie.</summary>
public sealed class CategoryRequest
{
    [Required, MaxLength(30), RegularExpression("^[A-Z0-9_-]{2,30}$",
        ErrorMessage = "code doit etre en majuscules (lettres, chiffres, - et _), 2 a 30 caracteres.")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}

/// <summary>Filtres de recherche de categories.</summary>
public sealed class CategoryQuery : PageQuery
{
    [MaxLength(120)]
    public string? Search { get; set; }

    public bool? IsActive { get; set; }
}

// ------------------------------ Produits ------------------------------

/// <summary>Produit expose par l'API.</summary>
public sealed class ProductResponse
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "XAF";
    public string Unit { get; set; } = "piece";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation d'un produit.</summary>
public sealed class CreateProductRequest
{
    [Required, MaxLength(40), RegularExpression("^[A-Za-z0-9._-]{3,40}$",
        ErrorMessage = "sku doit contenir 3 a 40 caracteres alphanumeriques, point, tiret ou underscore.")]
    public string Sku { get; set; } = string.Empty;

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public Guid CategoryId { get; set; }

    [Range(0.01, 1_000_000_000, ErrorMessage = "unitPrice doit etre strictement positif.")]
    public decimal UnitPrice { get; set; }

    [MaxLength(20)]
    public string Unit { get; set; } = "piece";
}

/// <summary>Mise a jour d'un produit (hors prix et activation).</summary>
public sealed class UpdateProductRequest
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public Guid CategoryId { get; set; }

    [MaxLength(20)]
    public string Unit { get; set; } = "piece";
}

/// <summary>Revalorisation d'un produit.</summary>
public sealed class ChangePriceRequest
{
    [Range(0.01, 1_000_000_000, ErrorMessage = "unitPrice doit etre strictement positif.")]
    public decimal UnitPrice { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }
}

/// <summary>Activation / desactivation d'un produit ou d'une categorie.</summary>
public sealed class SetActiveRequest
{
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

    /// <summary>Prix minimum inclus.</summary>
    [Range(0, 1_000_000_000)]
    public decimal? MinPrice { get; set; }

    /// <summary>Prix maximum inclus.</summary>
    [Range(0, 1_000_000_000)]
    public decimal? MaxPrice { get; set; }

    /// <summary>Tri : <c>name</c>, <c>price</c>, <c>sku</c> ou <c>createdAt</c>, suffixe <c>_desc</c> possible.</summary>
    [MaxLength(30)]
    public string? Sort { get; set; }
}

/// <summary>Entree de l'historique de prix.</summary>
public sealed class PriceHistoryResponse
{
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}

// ------------------- Tarification (appel inter-services) -------------------

/// <summary>
/// Demande de tarification envoyee par order-service lors de la confirmation
/// d'une commande (premiere etape de la saga).
/// </summary>
public sealed class PricingRequest
{
    /// <summary>Produits dont le prix courant est demande.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins un identifiant de produit est requis.")]
    [MaxLength(200, ErrorMessage = "200 produits maximum par appel.")]
    public List<Guid> ProductIds { get; set; } = [];
}

/// <summary>Prix courant d'un produit.</summary>
public sealed class PricingItem
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = "piece";
    public bool IsActive { get; set; }
}

/// <summary>Reponse de tarification.</summary>
public sealed class PricingResponse
{
    /// <summary>Devise commune.</summary>
    public string Currency { get; set; } = "XAF";

    /// <summary>Prix trouves.</summary>
    public List<PricingItem> Items { get; set; } = [];

    /// <summary>Identifiants demandes mais inconnus du catalogue.</summary>
    public List<Guid> Missing { get; set; } = [];

    /// <summary>Date de la cotation.</summary>
    public DateTimeOffset QuotedAt { get; set; } = DateTimeOffset.UtcNow;
}
