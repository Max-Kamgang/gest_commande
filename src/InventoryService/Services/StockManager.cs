using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Domain;
using GestionCommandes.InventoryService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.InventoryService.Services;

/// <summary>Gestion du stock : consultation, entrees, sorties, ajustements et seuils.</summary>
public sealed class StockManager(
    InventoryDbContext context,
    StockLedger ledger,
    ILogger<StockManager> logger)
{
    /// <summary>Liste paginee des lignes de stock.</summary>
    public async Task<PagedResult<StockItemResponse>> SearchAsync(
        StockQuery query,
        CancellationToken cancellationToken)
    {
        var source = context.StockItems
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .AsQueryable();

        if (query.ProductId is { } productId)
        {
            source = source.Where(s => s.ProductId == productId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            source = source.Where(s => s.WarehouseId == warehouseId);
        }

        // QuantityAvailable est une propriete calculee : la comparaison est traduite
        // explicitement en SQL sur les deux colonnes persistees.
        if (query.LowStockOnly == true)
        {
            source = source.Where(s => s.QuantityOnHand - s.QuantityReserved <= s.LowStockThreshold);
        }

        source = query.Sort?.ToLowerInvariant() switch
        {
            "available" => source.OrderBy(s => s.QuantityOnHand - s.QuantityReserved),
            "available_desc" => source.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved),
            "onhand" => source.OrderBy(s => s.QuantityOnHand),
            "onhand_desc" => source.OrderByDescending(s => s.QuantityOnHand),
            "updatedat" => source.OrderBy(s => s.UpdatedAt),
            "updatedat_desc" => source.OrderByDescending(s => s.UpdatedAt),
            _ => source.OrderBy(s => s.Warehouse!.Code).ThenBy(s => s.ProductId)
        };

        return await source.ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Disponibilite consolidee d'un produit sur l'ensemble des entrepots.</summary>
    public async Task<AvailabilityResponse> GetAvailabilityAsync(
        Guid productId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var items = await context.StockItems
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => s.ProductId == productId && (warehouseId == null || s.WarehouseId == warehouseId))
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            throw new NotFoundException("Le stock du produit", productId);
        }

        return new AvailabilityResponse
        {
            ProductId = productId,
            TotalOnHand = items.Sum(s => s.QuantityOnHand),
            TotalReserved = items.Sum(s => s.QuantityReserved),
            TotalAvailable = items.Sum(s => s.QuantityAvailable),
            IsLowStock = items.Any(s => s.IsLowStock),
            ByWarehouse = items
                .OrderBy(s => s.Warehouse?.Code)
                .Select(ToResponse)
                .ToList()
        };
    }

    /// <summary>Lignes de stock au niveau du seuil d'alerte ou en dessous.</summary>
    public async Task<PagedResult<StockItemResponse>> GetLowStockAsync(
        PageQuery query,
        CancellationToken cancellationToken)
        => await context.StockItems
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Where(s => s.QuantityOnHand - s.QuantityReserved <= s.LowStockThreshold)
            .OrderBy(s => s.QuantityOnHand - s.QuantityReserved)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);

    /// <summary>
    /// Entree en stock. Cree la ligne de stock si le couple produit/entrepot est nouveau.
    /// </summary>
    public async Task<StockItemResponse> AddEntryAsync(
        StockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await GetActiveWarehouseAsync(request.WarehouseId, cancellationToken);
        var item = await context.StockItems
            .Include(s => s.Warehouse)
            .FirstOrDefaultAsync(
                s => s.ProductId == request.ProductId && s.WarehouseId == request.WarehouseId,
                cancellationToken);

        if (item is null)
        {
            item = new StockItem
            {
                ProductId = request.ProductId,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                ProductLabel = string.IsNullOrWhiteSpace(request.ProductLabel)
                    ? null
                    : request.ProductLabel.Trim(),
                QuantityOnHand = 0,
                QuantityReserved = 0,
                LowStockThreshold = request.LowStockThreshold ?? 5
            };

            context.StockItems.Add(item);
        }
        else
        {
            if (request.LowStockThreshold is { } threshold)
            {
                item.LowStockThreshold = threshold;
            }

            // Le libelle le plus recent gagne : le catalogue peut avoir ete renomme.
            if (!string.IsNullOrWhiteSpace(request.ProductLabel))
            {
                item.ProductLabel = request.ProductLabel.Trim();
            }
        }

        item.QuantityOnHand += request.Quantity;

        ledger.RecordMovement(item, MovementType.Entree, request.Quantity, request.Reference, request.Reason);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Entree de {Quantity} unite(s) du produit {ProductId} dans {Warehouse} (nouveau stock : {OnHand})",
            request.Quantity,
            request.ProductId,
            warehouse.Code,
            item.QuantityOnHand);

        return ToResponse(item);
    }

    /// <summary>
    /// Sortie definitive hors commande (casse, perte, consommation interne).
    /// Ne peut pas entamer la part reservee aux commandes confirmees.
    /// </summary>
    public async Task<StockItemResponse> AddExitAsync(
        StockMovementRequest request,
        CancellationToken cancellationToken)
    {
        var item = await GetStockItemAsync(request.ProductId, request.WarehouseId, cancellationToken);

        if (request.Quantity > item.QuantityAvailable)
        {
            throw new ConflictException(
                $"Sortie impossible : {request.Quantity} unite(s) demandee(s) mais seulement " +
                $"{item.QuantityAvailable} disponible(s) (dont {item.QuantityReserved} reservee(s) " +
                "pour des commandes confirmees).",
                "INSUFFICIENT_STOCK",
                new Dictionary<string, object?>
                {
                    ["requested"] = request.Quantity,
                    ["available"] = item.QuantityAvailable,
                    ["reserved"] = item.QuantityReserved
                });
        }

        item.QuantityOnHand -= request.Quantity;

        ledger.RecordMovement(item, MovementType.Sortie, request.Quantity, request.Reference, request.Reason);

        await context.SaveChangesAsync(cancellationToken);
        ledger.NotifyIfLowStock(item, request.Reference);

        logger.LogInformation(
            "Sortie de {Quantity} unite(s) du produit {ProductId} (nouveau stock : {OnHand})",
            request.Quantity,
            request.ProductId,
            item.QuantityOnHand);

        return ToResponse(item);
    }

    /// <summary>
    /// Ajustement d'inventaire : la quantite physique est redefinie a la valeur comptee.
    /// La nouvelle quantite ne peut pas descendre sous la part deja reservee.
    /// </summary>
    public async Task<StockItemResponse> AdjustAsync(
        StockAdjustmentRequest request,
        CancellationToken cancellationToken)
    {
        var item = await GetStockItemAsync(request.ProductId, request.WarehouseId, cancellationToken);

        if (request.QuantityOnHand < item.QuantityReserved)
        {
            throw new ConflictException(
                $"Ajustement refuse : {item.QuantityReserved} unite(s) sont reservees pour des commandes " +
                $"confirmees, la quantite physique ne peut pas etre ramenee a {request.QuantityOnHand}.",
                "ADJUSTMENT_BELOW_RESERVED",
                new Dictionary<string, object?>
                {
                    ["requested"] = request.QuantityOnHand,
                    ["reserved"] = item.QuantityReserved
                });
        }

        var delta = request.QuantityOnHand - item.QuantityOnHand;

        if (delta == 0)
        {
            throw new ConflictException(
                $"Le stock est deja a {request.QuantityOnHand} unite(s) : aucun ajustement necessaire.",
                "ADJUSTMENT_UNCHANGED");
        }

        item.QuantityOnHand = request.QuantityOnHand;

        ledger.RecordMovement(item, MovementType.Ajustement, Math.Abs(delta), null, request.Reason);

        await context.SaveChangesAsync(cancellationToken);
        ledger.NotifyIfLowStock(item);

        logger.LogWarning(
            "Ajustement d'inventaire du produit {ProductId} : {Delta:+#;-#;0} unite(s), nouveau stock {OnHand} ({Reason})",
            request.ProductId,
            delta,
            item.QuantityOnHand,
            request.Reason);

        return ToResponse(item);
    }

    /// <summary>Modifie le seuil d'alerte d'une ligne de stock.</summary>
    public async Task<StockItemResponse> SetThresholdAsync(
        Guid productId,
        Guid warehouseId,
        int threshold,
        CancellationToken cancellationToken)
    {
        var item = await GetStockItemAsync(productId, warehouseId, cancellationToken);

        item.LowStockThreshold = threshold;

        await context.SaveChangesAsync(cancellationToken);
        ledger.NotifyIfLowStock(item);

        logger.LogInformation(
            "Seuil d'alerte du produit {ProductId} porte a {Threshold}",
            productId,
            threshold);

        return ToResponse(item);
    }

    /// <summary>Journal pagine des mouvements de stock.</summary>
    public async Task<PagedResult<StockMovementResponse>> SearchMovementsAsync(
        MovementQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new BadRequestException("from ne peut pas etre posterieur a to.", "INVALID_DATE_RANGE");
        }

        var source = context.StockMovements.AsNoTracking().AsQueryable();

        if (query.ProductId is { } productId)
        {
            source = source.Where(m => m.ProductId == productId);
        }

        if (query.WarehouseId is { } warehouseId)
        {
            source = source.Where(m => m.WarehouseId == warehouseId);
        }

        if (query.Type is { } type)
        {
            source = source.Where(m => m.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Reference))
        {
            var reference = query.Reference.Trim();
            source = source.Where(m => m.Reference == reference);
        }

        if (query.From is { } fromDate)
        {
            source = source.Where(m => m.OccurredAt >= fromDate);
        }

        if (query.To is { } toDate)
        {
            source = source.Where(m => m.OccurredAt <= toDate);
        }

        return await source
            .OrderByDescending(m => m.OccurredAt)
            .ThenByDescending(m => m.Id)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    // ------------------------------- Helpers -------------------------------

    private async Task<StockItem> GetStockItemAsync(
        Guid productId,
        Guid warehouseId,
        CancellationToken cancellationToken)
        => await context.StockItems
               .Include(s => s.Warehouse)
               .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, cancellationToken)
           ?? throw new NotFoundException(
               "Le stock du produit",
               $"{productId} dans l'entrepot {warehouseId}");

    private async Task<Warehouse> GetActiveWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId, cancellationToken)
                        ?? throw new BadRequestException(
                            $"L'entrepot '{warehouseId}' est inconnu.",
                            "UNKNOWN_WAREHOUSE");

        if (!warehouse.IsActive)
        {
            throw new ConflictException(
                $"L'entrepot '{warehouse.Code}' est desactive : aucun mouvement n'y est autorise.",
                "WAREHOUSE_INACTIVE");
        }

        return warehouse;
    }

    internal static StockItemResponse ToResponse(StockItem item) => new()
    {
        Id = item.Id,
        ProductId = item.ProductId,
        ProductLabel = item.ProductLabel,
        WarehouseId = item.WarehouseId,
        WarehouseCode = item.Warehouse?.Code ?? string.Empty,
        WarehouseName = item.Warehouse?.Name ?? string.Empty,
        QuantityOnHand = item.QuantityOnHand,
        QuantityReserved = item.QuantityReserved,
        QuantityAvailable = item.QuantityAvailable,
        LowStockThreshold = item.LowStockThreshold,
        IsLowStock = item.IsLowStock,
        UpdatedAt = item.UpdatedAt
    };

    private static StockMovementResponse ToResponse(StockMovement movement) => new()
    {
        Id = movement.Id,
        ProductId = movement.ProductId,
        WarehouseId = movement.WarehouseId,
        Type = movement.Type,
        Quantity = movement.Quantity,
        QuantityOnHandAfter = movement.QuantityOnHandAfter,
        QuantityReservedAfter = movement.QuantityReservedAfter,
        Reference = movement.Reference,
        Reason = movement.Reason,
        PerformedBy = movement.PerformedBy,
        OccurredAt = movement.OccurredAt
    };
}
