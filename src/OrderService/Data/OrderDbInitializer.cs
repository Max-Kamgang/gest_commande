using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.OrderService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Data;

/// <summary>
/// Migre la base puis injecte les clients de demonstration.
/// Les identifiants proviennent de <see cref="DemoData"/> : le compte
/// <c>client@micro.local</c> d'identity-service porte le <c>customer_id</c> du
/// premier client, ce qui rend la demonstration coherente de bout en bout.
/// </summary>
public static class OrderDbInitializer
{
    /// <summary>Migre et alimente la base des commandes.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrderDbContext>>();

        logger.LogInformation("Application des migrations EF Core (order-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Customers.AnyAsync(cancellationToken))
        {
            return;
        }

        context.Customers.AddRange(DemoData.Customers.Select(c => new Customer
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name,
            Email = c.Email,
            Phone = c.Phone,
            City = c.City,
            Address = c.Address,
            IsActive = true
        }));

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{Count} clients de demonstration crees.", DemoData.Customers.Length);
    }
}
