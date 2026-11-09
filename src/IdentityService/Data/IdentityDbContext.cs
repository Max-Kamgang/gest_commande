using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.IdentityService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Data;

/// <summary>
/// Base SQLite <b>propre a identity-service</b> (fichier <c>identity-service.db</c>).
/// Aucune autre base n'est partagee : c'est la regle « une base par microservice ».
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Comptes utilisateurs.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>Roles attribues.</summary>
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.UserName).IsRequired();
            entity.Property(u => u.Email).IsRequired();
            entity.Property(u => u.FullName).IsRequired();
            entity.Property(u => u.PasswordHash).IsRequired();

            // Unicite de l'identifiant de connexion (insensible a la casse cote SQLite).
            entity.HasIndex(u => u.UserName).IsUnique();
            entity.HasIndex(u => u.Email);

            entity.HasMany(u => u.Roles)
                .WithOne(r => r.User!)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(r => new { r.UserId, r.Role });
            entity.Property(r => r.Role).IsRequired();
        });
    }
}
