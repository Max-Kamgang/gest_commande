using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Data;

/// <summary>
/// Applique les migrations EF Core au demarrage puis injecte les comptes de
/// demonstration (un par role). Idempotent : relancer le service ne cree pas de doublon.
/// </summary>
public static class IdentityDbInitializer
{
    /// <summary>Mot de passe commun aux comptes de demonstration (documente dans le README).</summary>
    private const string DemoPassword = "Micro@2025!";

    /// <summary>Migre la base et alimente les donnees de demonstration.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();

        logger.LogInformation("Application des migrations EF Core (identity-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        if (await context.Users.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Comptes deja presents : aucun seeding necessaire.");
            return;
        }

        var hash = PasswordHasher.Hash(DemoPassword);

        var seeds = new List<AppUser>
        {
            Build(DemoIds.Users.Admin, "admin@micro.local", "Administrateur plateforme", hash, [Roles.Admin]),
            Build(DemoIds.Users.Gestionnaire, "gestion@micro.local", "Gestionnaire logistique", hash, [Roles.Gestionnaire]),
            Build(DemoIds.Users.Livreur, "livreur@micro.local", "Paul Mbarga", hash, [Roles.Livreur],
                courierId: DemoIds.Couriers.PaulMbarga),
            Build(DemoIds.Users.Livreur2, "livreur2@micro.local", "Aline Njoya", hash, [Roles.Livreur],
                courierId: DemoIds.Couriers.AlineNjoya),
            Build(DemoIds.Users.Client, "client@micro.local", "Ets Kamgang & Fils", hash, [Roles.Client],
                customerId: DemoIds.Customers.EtsKamgang)
        };

        context.Users.AddRange(seeds);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "{Count} comptes de demonstration crees (mot de passe commun documente dans le README).",
            seeds.Count);
    }

    private static AppUser Build(
        Guid id,
        string userName,
        string fullName,
        string passwordHash,
        string[] roles,
        Guid? courierId = null,
        Guid? customerId = null) => new()
    {
        Id = id,
        UserName = userName,
        Email = userName,
        FullName = fullName,
        PasswordHash = passwordHash,
        IsActive = true,
        CourierId = courierId,
        CustomerId = customerId,
        CreatedAt = DateTimeOffset.UtcNow,
        Roles = roles.Select(r => new UserRole { UserId = id, Role = r }).ToList()
    };
}
