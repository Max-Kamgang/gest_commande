using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.InventoryService.Domain;

namespace GestionCommandes.InventoryService.Dtos;

// ------------------------------ Entrepots ------------------------------

/// <summary>Entrepot expose par l'API.</summary>
public sealed class WarehouseResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public int StockItemCount { get; set; }
    public int TotalQuantityOnHand { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Creation / mise a jour d'un entrepot.</summary>
public sealed class WarehouseRequest
{
    [Required, MaxLength(20), RegularExpression("^[A-Z0-9-]{2,20}$",
        ErrorMessage = "code doit etre en majuscules (lettres, chiffres, tiret), 2 a 20 caracteres.")]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string City { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Address { get; set; }
}

/// <summary>Filtres de recherche d'entrepots.</summary>
public sealed class WarehouseQuery : PageQuery
{
    [MaxLength(120)]
    public string? Search { get; set; }

    public bool? IsActive { get; set; }
}

// -------------------------------- Stock --------------------------------

/// <summary>Ligne de stock exposee par l'API.</summary>
public sealed class StockItemResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ProductLabel { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int QuantityAvailable { get; set; }
    public int LowStockThreshold { get; set; }
    public bool IsLowStock { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Disponibilite consolidee d'un produit, tous entrepots confondus.</summary>
public sealed class AvailabilityResponse
{
    public Guid ProductId { get; set; }
    public int TotalOnHand { get; set; }
    public int TotalReserved { get; set; }
    public int TotalAvailable { get; set; }
    public bool IsLowStock { get; set; }
    public List<StockItemResponse> ByWarehouse { get; set; } = [];
}

/// <summary>Entree ou sortie de stock.</summary>
public sealed class StockMovementRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public Guid WarehouseId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "quantity doit etre un entier strictement positif.")]
    public int Quantity { get; set; }

    /// <summary>Reference metier (bon de reception, numero de commande...).</summary>
    [MaxLength(60)]
    public string? Reference { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Seuil d'alerte a appliquer si la ligne de stock est creee par cette entree.</summary>
    [Range(0, 1_000_000)]
    public int? LowStockThreshold { get; set; }

    /// <summary>
    /// Libelle du produit, recopie du catalogue par l'appelant. Facultatif :
    /// il ne sert qu'a rendre les alertes de stock bas lisibles.
    /// </summary>
    [MaxLength(180)]
    public string? ProductLabel { get; set; }
}

/// <summary>Ajustement d'inventaire : la quantite physique est redefinie.</summary>
public sealed class StockAdjustmentRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Required]
    public Guid WarehouseId { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "quantityOnHand doit etre un entier positif ou nul.")]
    public int QuantityOnHand { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "reason est obligatoire pour un ajustement d'inventaire.")]
    [MaxLength(300)]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Modification du seuil d'alerte.</summary>
public sealed class ThresholdRequest
{
    [Range(0, 1_000_000)]
    public int LowStockThreshold { get; set; }
}

/// <summary>Filtres de recherche de lignes de stock.</summary>
public sealed class StockQuery : PageQuery
{
    public Guid? ProductId { get; set; }

    public Guid? WarehouseId { get; set; }

    /// <summary>Ne renvoyer que les lignes au niveau du seuil d'alerte ou en dessous.</summary>
    public bool? LowStockOnly { get; set; }

    /// <summary>Tri : <c>available</c>, <c>onhand</c> ou <c>updatedat</c>, suffixe <c>_desc</c> possible.</summary>
    [MaxLength(30)]
    public string? Sort { get; set; }
}

/// <summary>Mouvement de stock expose par l'API.</summary>
public sealed class StockMovementResponse
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public MovementType Type { get; set; }
    public int Quantity { get; set; }
    public int QuantityOnHandAfter { get; set; }
    public int QuantityReservedAfter { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}

/// <summary>Filtres de recherche de mouvements.</summary>
public sealed class MovementQuery : PageQuery
{
    public Guid? ProductId { get; set; }

    public Guid? WarehouseId { get; set; }

    public MovementType? Type { get; set; }

    [MaxLength(60)]
    public string? Reference { get; set; }

    /// <summary>Debut de periode (inclus, UTC).</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Fin de periode (incluse, UTC).</summary>
    public DateTimeOffset? To { get; set; }
}

// ----------------------------- Reservations -----------------------------

/// <summary>Ligne a reserver.</summary>
public sealed class ReservationLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "quantity doit etre un entier strictement positif.")]
    public int Quantity { get; set; }

    /// <summary>
    /// Entrepot impose. Si absent, le service repartit automatiquement la quantite
    /// sur les entrepots actifs ayant du disponible (du plus fourni au moins fourni).
    /// </summary>
    public Guid? WarehouseId { get; set; }

    /// <summary>Libelle du produit, recopie du catalogue par order-service. Facultatif.</summary>
    [MaxLength(180)]
    public string? ProductLabel { get; set; }
}

/// <summary>Demande de reservation de stock (etape 2 de la saga de commande).</summary>
public sealed class ReservationRequest
{
    /// <summary>Reference metier : numero de commande. Sert de cle d'idempotence.</summary>
    [Required(AllowEmptyStrings = false), MaxLength(60)]
    public string Reference { get; set; } = string.Empty;

    [Required, MinLength(1, ErrorMessage = "Au moins une ligne est requise.")]
    [MaxLength(200, ErrorMessage = "200 lignes maximum par reservation.")]
    public List<ReservationLineRequest> Lines { get; set; } = [];
}

/// <summary>Ligne reservee.</summary>
public sealed class ReservationLineResponse
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>Reservation exposee par l'API.</summary>
public sealed class ReservationResponse
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public ReservationStatus Status { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public List<ReservationLineResponse> Lines { get; set; } = [];
}

/// <summary>Detail d'une indisponibilite, renvoye dans le 409 de reservation.</summary>
public sealed class ShortageDetail
{
    public Guid ProductId { get; set; }
    public int Requested { get; set; }
    public int Available { get; set; }
}
