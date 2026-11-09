using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Data;

/// <summary>
/// Migre la base puis injecte les categories et produits de demonstration.
/// Les identifiants proviennent de <see cref="DemoData"/> : ce sont les memes que
/// ceux utilises par inventory-service pour le stock initial.
/// </summary>
public static class CatalogDbInitializer
{
    /// <summary>Migre et alimente la base du catalogue.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogDbContext>>();

        logger.LogInformation("Application des migrations EF Core (catalog-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        if (!await context.Categories.AnyAsync(cancellationToken))
        {
            context.Categories.AddRange(DemoData.Categories.Select(c => new Category
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                Description = c.Description,
                IsActive = true
            }));

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} categories de demonstration creees.", DemoData.Categories.Length);
        }

        if (!await context.Products.AnyAsync(cancellationToken))
        {
            context.Products.AddRange(DemoData.Products.Select(p => new Product
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                Description = p.Description,
                CategoryId = p.CategoryId,
                UnitPrice = p.UnitPrice,
                Currency = DemoData.Currency,
                Unit = p.Unit,
                IsActive = true
            }));

            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Count} produits de demonstration crees.", DemoData.Products.Length);
        }
    }
}
