using GestionCommandes.IdentityService.Domain;
using GestionCommandes.Shared.Data;
using GestionCommandes.Shared.Domain;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Data;

/// <summary>Base SQLite de identity-service (<c>identity-service.db</c>).</summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Comptes utilisateurs.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>Roles attribues aux comptes.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.UserName).IsRequired();
            entity.HasIndex(u => u.UserName).IsUnique();

            entity.HasMany(u => u.Roles)
                .WithOne(r => r.User!)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            // Cle composee : un role n'est attribue qu'une fois par compte.
            entity.HasKey(r => new { r.UserId, r.Role });
        });
    }
}

/// <summary>
/// Cree la base si elle n'existe pas et insere les comptes de demonstration.
///
/// On utilise <c>EnsureCreated</c> et non les migrations EF : le schema est cree
/// d'un bloc au premier demarrage. C'est suffisant ici (base SQLite dediee, jeu de
/// donnees de demonstration) et cela evite d'embarquer l'outillage de migration
/// dans les images.
/// </summary>
public static class IdentityDbInitializer
{
    /// <summary>Prepare la base du service.</summary>
    /// <param name="services">Fournisseur de services de l'application.</param>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<IdentityDbContext>>();

        await context.Database.EnsureCreatedAsync();

        if (await context.Users.AnyAsync())
        {
            return;
        }

        // Un compte par role, tous avec le meme mot de passe de demonstration.
        var hash = PasswordHasher.Hash(DemoData.DemoPassword);

        context.Users.AddRange(
            new AppUser
            {
                Id = DemoIds.Users.Admin,
                UserName = "admin@micro.local",
                Email = "admin@micro.local",
                FullName = "Administrateur plateforme",
                PasswordHash = hash,
                Roles = [new UserRole { Role = Roles.Admin }]
            },
            new AppUser
            {
                Id = DemoIds.Users.Gestionnaire,
                UserName = "gestionnaire@micro.local",
                Email = "gestionnaire@micro.local",
                FullName = "Gestionnaire des commandes",
                PasswordHash = hash,
                Roles = [new UserRole { Role = Roles.Gestionnaire }]
            },
            new AppUser
            {
                Id = DemoIds.Users.Livreur,
                UserName = "livreur@micro.local",
                Email = "livreur@micro.local",
                FullName = "Paul Mbarga",
                PasswordHash = hash,
                // Lie au livreur LIV-001 de delivery-service.
                CourierId = DemoIds.Couriers.PaulMbarga,
                Roles = [new UserRole { Role = Roles.Livreur }]
            },
            new AppUser
            {
                Id = DemoIds.Users.Client,
                UserName = "client@micro.local",
                Email = "client@micro.local",
                FullName = "Ets Kamgang et Fils",
                PasswordHash = hash,
                // Lie au client CLI-001 de order-service.
                CustomerId = DemoIds.Customers.EtsKamgang,
                Roles = [new UserRole { Role = Roles.Client }]
            });

        await context.SaveChangesAsync();

        logger.LogInformation(
            "4 comptes de demonstration crees (mot de passe commun : {Password}).", DemoData.DemoPassword);
    }
}
