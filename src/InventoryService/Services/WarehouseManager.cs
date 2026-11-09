using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Domain;
using GestionCommandes.InventoryService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.InventoryService.Services;

/// <summary>Gestion des entrepots.</summary>
public sealed class WarehouseManager(InventoryDbContext context, ILogger<WarehouseManager> logger)
{
    /// <summary>Liste paginee des entrepots.</summary>
    public async Task<PagedResult<WarehouseResponse>> SearchAsync(
        WarehouseQuery query,
        CancellationToken cancellationToken)
    {
        var source = context.Warehouses
            .AsNoTracking()
            .Include(w => w.StockItems)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(w =>
                EF.Functions.Like(w.Code, $"%{term}%") ||
                EF.Functions.Like(w.Name, $"%{term}%") ||
                EF.Functions.Like(w.City, $"%{term}%"));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(w => w.IsActive == isActive);
        }

        return await source
            .OrderBy(w => w.Code)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere un entrepot.</summary>
    public async Task<WarehouseResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .AsNoTracking()
            .Include(w => w.StockItems)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("L'entrepot", id);

        return ToResponse(warehouse);
    }

    /// <summary>Cree un entrepot.</summary>
    public async Task<WarehouseResponse> CreateAsync(WarehouseRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Warehouses.AnyAsync(w => w.Code == code, cancellationToken))
        {
            throw new ConflictException($"Un entrepot porte deja le code '{code}'.", "WAREHOUSE_CODE_TAKEN");
        }

        var warehouse = new Warehouse
        {
            Code = code,
            Name = request.Name.Trim(),
            City = request.City.Trim(),
            Address = request.Address?.Trim(),
            IsActive = true
        };

        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Entrepot {Code} cree", code);

        return ToResponse(warehouse);
    }

    /// <summary>Met a jour un entrepot.</summary>
    public async Task<WarehouseResponse> UpdateAsync(
        Guid id,
        WarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .Include(w => w.StockItems)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("L'entrepot", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Warehouses.AnyAsync(w => w.Code == code && w.Id != id, cancellationToken))
        {
            throw new ConflictException($"Un autre entrepot porte deja le code '{code}'.", "WAREHOUSE_CODE_TAKEN");
        }

        warehouse.Code = code;
        warehouse.Name = request.Name.Trim();
        warehouse.City = request.City.Trim();
        warehouse.Address = request.Address?.Trim();
        warehouse.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(warehouse);
    }

    /// <summary>Active ou desactive un entrepot (un entrepot inactif n'est plus reservable).</summary>
    public async Task<WarehouseResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .Include(w => w.StockItems)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("L'entrepot", id);

        if (!isActive)
        {
            var reserved = warehouse.StockItems.Sum(s => s.QuantityReserved);

            if (reserved > 0)
            {
                throw new ConflictException(
                    $"L'entrepot '{warehouse.Code}' porte encore {reserved} unite(s) reservee(s). " +
                    "Expediez ou annulez les commandes concernees avant de le desactiver.",
                    "WAREHOUSE_HAS_RESERVATIONS",
                    new Dictionary<string, object?> { ["reservedQuantity"] = reserved });
            }
        }

        warehouse.IsActive = isActive;
        warehouse.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Entrepot {Code} {Etat}", warehouse.Code, isActive ? "active" : "desactive");

        return ToResponse(warehouse);
    }

    /// <summary>Supprime un entrepot sans stock.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var warehouse = await context.Warehouses
            .Include(w => w.StockItems)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new NotFoundException("L'entrepot", id);

        if (warehouse.StockItems.Count > 0)
        {
            throw new ConflictException(
                $"L'entrepot '{warehouse.Code}' porte encore {warehouse.StockItems.Count} ligne(s) de stock. " +
                "Videz-le ou desactivez-le au lieu de le supprimer.",
                "WAREHOUSE_NOT_EMPTY",
                new Dictionary<string, object?> { ["stockItemCount"] = warehouse.StockItems.Count });
        }

        context.Warehouses.Remove(warehouse);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Entrepot {Code} supprime", warehouse.Code);
    }

    private static WarehouseResponse ToResponse(Warehouse warehouse) => new()
    {
        Id = warehouse.Id,
        Code = warehouse.Code,
        Name = warehouse.Name,
        City = warehouse.City,
        Address = warehouse.Address,
        IsActive = warehouse.IsActive,
        StockItemCount = warehouse.StockItems.Count,
        TotalQuantityOnHand = warehouse.StockItems.Sum(s => s.QuantityOnHand),
        CreatedAt = warehouse.CreatedAt,
        UpdatedAt = warehouse.UpdatedAt
    };
}
