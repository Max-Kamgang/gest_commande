using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.CatalogService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Data;

/// <summary>Base SQLite propre a catalog-service (<c>catalog-service.db</c>).</summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<PriceHistoryEntry> PriceHistory => Set<PriceHistoryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).IsRequired();
            entity.Property(c => c.Name).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();

            entity.HasMany(c => c.Products)
                .WithOne(p => p.Category!)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Sku).IsRequired();
            entity.Property(p => p.Name).IsRequired();
            entity.Property(p => p.Currency).IsRequired();
            entity.Property(p => p.Unit).IsRequired();

            // Montant stocke en unites mineures : comparaisons et tris exacts sous SQLite.
            entity.Property(p => p.UnitPrice).HasMoneyConversion();

            entity.HasIndex(p => p.Sku).IsUnique();
            entity.HasIndex(p => p.CategoryId);
            entity.HasIndex(p => p.IsActive);

            entity.HasMany(p => p.PriceHistory)
                .WithOne(h => h.Product!)
                .HasForeignKey(h => h.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PriceHistoryEntry>(entity =>
        {
            entity.ToTable("PriceHistory");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.OldPrice).HasMoneyConversion();
            entity.Property(h => h.NewPrice).HasMoneyConversion();
            entity.HasIndex(h => new { h.ProductId, h.ChangedAt });
        });
    }
}
