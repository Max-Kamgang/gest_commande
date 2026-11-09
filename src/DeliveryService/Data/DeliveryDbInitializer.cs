using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.DeliveryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Data;

/// <summary>
/// Migre la base puis injecte les livreurs de demonstration. Leurs identifiants
/// correspondent aux claims <c>courier_id</c> des comptes <c>livreur@micro.local</c>
/// et <c>livreur2@micro.local</c> d'identity-service.
/// </summary>
public static class DeliveryDbInitializer
{
    /// <summary>Migre et alimente la base des livraisons.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DeliveryDbContext>>();

        logger.LogInformation("Application des migrations EF Core (delivery-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Couriers.AnyAsync(cancellationToken))
        {
            return;
        }

        context.Couriers.AddRange(DemoData.Couriers.Select(c => new Courier
        {
            Id = c.Id,
            Code = c.Code,
            FullName = c.FullName,
            Email = c.Email,
            Phone = c.Phone,
            Vehicle = c.Vehicle,
            Zone = c.Zone,
            MaxConcurrentDeliveries = 10,
            IsActive = true
        }));

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("{Count} livreurs de demonstration crees.", DemoData.Couriers.Length);
    }
}
