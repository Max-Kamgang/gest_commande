using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.InventoryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.InventoryService.Data;

/// <summary>
/// Migre la base puis injecte les entrepots et le stock initial de demonstration.
/// Les identifiants de produits proviennent de <see cref="DemoData"/> : ils
/// correspondent exactement a ceux de catalog-service.
/// </summary>
public static class InventoryDbInitializer
{
    /// <summary>Migre et alimente la base du stock.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<InventoryDbContext>>();

        logger.LogInformation("Application des migrations EF Core (inventory-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        if (!await context.Warehouses.AnyAsync(cancellationToken))
        {
            context.Warehouses.AddRange(DemoData.Warehouses.Select(w => new Warehouse
            {
                Id = w.Id,
                Code = w.Code,
                Name = w.Name,
                City = w.City,
                Address = w.Address,
                IsActive = true
            }));

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} entrepots de demonstration crees.", DemoData.Warehouses.Length);
        }

        if (!await context.StockItems.AnyAsync(cancellationToken))
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var seed in DemoData.Stocks)
            {
                var item = new StockItem
                {
                    ProductId = seed.ProductId,
                    WarehouseId = seed.WarehouseId,
                    QuantityOnHand = seed.Quantity,
                    QuantityReserved = 0,
                    LowStockThreshold = seed.LowStockThreshold,
                    CreatedAt = now
                };

                context.StockItems.Add(item);

                // Le stock initial est trace comme une entree : le journal est complet
                // des la premiere minute de vie du service.
                context.StockMovements.Add(new StockMovement
                {
                    StockItemId = item.Id,
                    ProductId = seed.ProductId,
                    WarehouseId = seed.WarehouseId,
                    Type = MovementType.Entree,
                    Quantity = seed.Quantity,
                    QuantityOnHandAfter = seed.Quantity,
                    QuantityReservedAfter = 0,
                    Reference = "STOCK-INITIAL",
                    Reason = "Stock initial de demonstration",
                    PerformedBy = "seeder",
                    OccurredAt = now
                });
            }

            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "{Count} lignes de stock initiales creees ({Total} unites au total).",
                DemoData.Stocks.Length,
                DemoData.Stocks.Sum(s => s.Quantity));
        }
    }
}
