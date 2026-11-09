using GestionCommandes.CatalogService.Domain;
using GestionCommandes.Shared.Data;
using GestionCommandes.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Data;

/// <summary>Base SQLite de catalog-service (<c>catalog-service.db</c>).</summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Produits et leur stock.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Historique des mouvements de stock.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>Reservations de stock.</summary>
    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    /// <summary>Lignes de reservation.</summary>
    public DbSet<StockReservationLine> ReservationLines => Set<StockReservationLine>();

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
                // Restrict : on n'efface pas des produits en supprimant une categorie.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Sku).IsRequired();
            entity.Property(p => p.Name).IsRequired();
            entity.Property(p => p.Unit).IsRequired();

            // Montant stocke en centimes : tris et filtres de prix exacts sous SQLite.
            entity.Property(p => p.UnitPrice).AsMoney();

            entity.HasIndex(p => p.Sku).IsUnique();
            entity.HasIndex(p => p.CategoryId);

            // Verrou optimiste sur le stock : EF Core compare la valeur avant ecriture.
            entity.Property(p => p.RowVersion).IsConcurrencyToken();
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements");
            entity.HasKey(m => m.Id);
            entity.HasIndex(m => new { m.ProductId, m.OccurredAt });
            entity.HasIndex(m => m.Reference);

            entity.HasOne(m => m.Product!)
                .WithMany()
                .HasForeignKey(m => m.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("Reservations");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Reference).IsRequired();
            // Reference unique : une commande ne peut pas etre reservee deux fois.
            entity.HasIndex(r => r.Reference).IsUnique();

            entity.HasMany(r => r.Lines)
                .WithOne(l => l.Reservation!)
                .HasForeignKey(l => l.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockReservationLine>(entity =>
        {
            entity.ToTable("ReservationLines");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.UnitPrice).AsMoney();
        });
    }

    /// <summary>
    /// Regenere le jeton de concurrence de chaque produit modifie, avant ecriture.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Product>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.RowVersion = Guid.NewGuid();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Cree la base si besoin et insere le catalogue de demonstration.
/// <c>EnsureCreated</c> remplace les migrations EF : le schema est cree d'un bloc
/// au premier demarrage, ce qui suffit pour une base SQLite dediee au service.
/// </summary>
public static class CatalogDbInitializer
{
    /// <summary>Prepare la base du service.</summary>
    /// <param name="services">Fournisseur de services de l'application.</param>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CatalogDbContext>>();

        await context.Database.EnsureCreatedAsync();

        if (await context.Products.AnyAsync())
        {
            return;
        }

        context.Categories.AddRange(DemoData.Categories.Select(c => new Category
        {
            Id = c.Id,
            Code = c.Code,
            Name = c.Name
        }));

        foreach (var demo in DemoData.Products)
        {
            context.Products.Add(new Product
            {
                Id = demo.Id,
                Sku = demo.Sku,
                Name = demo.Name,
                CategoryId = demo.CategoryId,
                UnitPrice = demo.UnitPrice,
                QuantityOnHand = demo.Stock,
                LowStockThreshold = demo.LowStockThreshold
            });

            // Le stock initial est trace comme une entree : l'historique est complet
            // des le depart, sans quantite apparue « de nulle part ».
            context.StockMovements.Add(new StockMovement
            {
                ProductId = demo.Id,
                Type = MovementType.Entree,
                Quantity = demo.Stock,
                QuantityOnHandAfter = demo.Stock,
                QuantityReservedAfter = 0,
                Reference = "STOCK-INITIAL",
                Reason = "Stock initial de demonstration",
                PerformedBy = "initialisation"
            });
        }

        await context.SaveChangesAsync();

        logger.LogInformation(
            "{Categories} categories et {Products} produits de demonstration crees.",
            DemoData.Categories.Length,
            DemoData.Products.Length);
    }
}
