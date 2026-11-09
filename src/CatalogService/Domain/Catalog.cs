using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.CatalogService.Domain;

/// <summary>Categorie de produits.</summary>
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code court unique (ex. <c>RESEAU</c>).</summary>
    [MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Une categorie desactivee masque ses produits du catalogue public.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Produits rattaches.</summary>
    public List<Product> Products { get; set; } = [];
}

/// <summary>Produit vendable.</summary>
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference article unique.</summary>
    [MaxLength(40)]
    public string Sku { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public Guid CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>
    /// Prix unitaire courant. C'est ce prix que order-service recupere a la
    /// confirmation et <b>fige</b> dans la ligne de commande : une revalorisation
    /// ulterieure du catalogue ne modifie pas les commandes deja confirmees.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Devise (XAF).</summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "XAF";

    /// <summary>Unite de vente (piece, carton, metre...).</summary>
    [MaxLength(20)]
    public string Unit { get; set; } = "piece";

    /// <summary>Un produit desactive ne peut plus etre commande.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Historique des changements de prix (tracabilite).</summary>
    public List<PriceHistoryEntry> PriceHistory { get; set; } = [];
}

/// <summary>Trace d'un changement de prix.</summary>
public class PriceHistoryEntry
{
    public long Id { get; set; }

    public Guid ProductId { get; set; }

    public Product? Product { get; set; }

    public decimal OldPrice { get; set; }

    public decimal NewPrice { get; set; }

    /// <summary>Auteur du changement (nom d'utilisateur du jeton).</summary>
    [MaxLength(160)]
    public string ChangedBy { get; set; } = "systeme";

    [MaxLength(300)]
    public string? Reason { get; set; }

    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
