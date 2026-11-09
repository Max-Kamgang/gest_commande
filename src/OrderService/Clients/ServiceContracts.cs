namespace GestionCommandes.OrderService.Clients;

// ---------------------------------------------------------------------------
// Contrats des appels sortants d'order-service.
//
// Chaque microservice declare SES propres contrats clients : rien n'est partage
// par assemblage entre services, seul le contrat HTTP l'est. C'est volontaire :
// un service doit pouvoir evoluer sans recompilation des autres.
// ---------------------------------------------------------------------------

/// <summary>Demande de tarification envoyee a catalog-service.</summary>
public sealed class PricingRequestDto
{
    public List<Guid> ProductIds { get; set; } = [];
}

/// <summary>Prix courant d'un produit, renvoye par catalog-service.</summary>
public sealed class PricingItemDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = "piece";
    public bool IsActive { get; set; }
}

/// <summary>Reponse de tarification de catalog-service.</summary>
public sealed class PricingResponseDto
{
    public string Currency { get; set; } = "XAF";
    public List<PricingItemDto> Items { get; set; } = [];
    public List<Guid> Missing { get; set; } = [];
    public DateTimeOffset QuotedAt { get; set; }
}

/// <summary>Ligne a reserver dans inventory-service.</summary>
public sealed class ReservationLineDto
{
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public Guid? WarehouseId { get; set; }

    /// <summary>
    /// Libelle du produit tel que connu du catalogue, transmis pour que les
    /// alertes de stock bas d'inventory-service restent lisibles sans qu'il ait
    /// a interroger catalog-service.
    /// </summary>
    public string? ProductLabel { get; set; }
}

/// <summary>Demande de reservation envoyee a inventory-service.</summary>
public sealed class ReservationRequestDto
{
    public string Reference { get; set; } = string.Empty;
    public List<ReservationLineDto> Lines { get; set; } = [];
}

/// <summary>Ligne reservee renvoyee par inventory-service.</summary>
public sealed class ReservedLineDto
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

/// <summary>Reservation renvoyee par inventory-service.</summary>
public sealed class ReservationResultDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public List<ReservedLineDto> Lines { get; set; } = [];
}

/// <summary>Ligne de bordereau de livraison envoyee a delivery-service.</summary>
public sealed class DeliveryLineDto
{
    public Guid ProductId { get; set; }
    public string? Sku { get; set; }
    public string? ProductName { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Demande de creation de livraison envoyee a delivery-service.</summary>
public sealed class CreateDeliveryRequestDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? ShippingAddress { get; set; }
    public string? ShippingCity { get; set; }
    public List<DeliveryLineDto> Lines { get; set; } = [];
}

/// <summary>Livraison renvoyee par delivery-service.</summary>
public sealed class DeliveryResultDto
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? CourierId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
