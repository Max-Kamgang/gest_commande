using GestionCommandes.OrderService.Domain;
using GestionCommandes.Shared.Data;
using GestionCommandes.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Data;

/// <summary>Base SQLite de order-service (<c>order-service.db</c>).</summary>
public class OrderDbContext(DbContextOptions<OrderDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Clients.</summary>
    public DbSet<Customer> Customers => Set<Customer>();

    /// <summary>Commandes.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Lignes de commande.</summary>
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    /// <summary>Historique des etats.</summary>
    public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();

    /// <summary>Compteurs de numerotation.</summary>
    public DbSet<OrderSequence> OrderSequences => Set<OrderSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).IsRequired();
            entity.Property(c => c.Name).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();

            entity.HasMany(c => c.Orders)
                .WithOne(o => o.Customer!)
                .HasForeignKey(o => o.CustomerId)
                // Restrict : on ne supprime pas des commandes avec un client.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Number).IsRequired();
            entity.HasIndex(o => o.Number).IsUnique();
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => o.CustomerId);

            entity.Property(o => o.SubTotal).AsMoney();
            entity.Property(o => o.VatAmount).AsMoney();
            entity.Property(o => o.Total).AsMoney();

            entity.Property(o => o.RowVersion).IsConcurrencyToken();

            entity.HasMany(o => o.Lines)
                .WithOne(l => l.Order!)
                .HasForeignKey(l => l.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(o => o.History)
                .WithOne(h => h.Order!)
                .HasForeignKey(h => h.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.HasKey(l => l.Id);
            entity.Property(l => l.UnitPrice).AsMoney();
            entity.Property(l => l.LineTotal).AsMoney();
            entity.HasIndex(l => new { l.OrderId, l.ProductId });
        });

        modelBuilder.Entity<OrderHistoryEntry>(entity =>
        {
            entity.ToTable("OrderHistory");
            entity.HasKey(h => h.Id);
            entity.HasIndex(h => new { h.OrderId, h.ChangedAt });
        });

        modelBuilder.Entity<OrderSequence>(entity =>
        {
            entity.ToTable("OrderSequences");
            entity.HasKey(s => s.Year);
        });
    }

    /// <summary>Regenere le jeton de concurrence de chaque commande modifiee.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Order>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.RowVersion = Guid.NewGuid();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Cree la base si besoin et insere les clients de demonstration.</summary>
public static class OrderDbInitializer
{
    /// <summary>Prepare la base du service.</summary>
    /// <param name="services">Fournisseur de services de l'application.</param>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrderDbContext>>();

        await context.Database.EnsureCreatedAsync();

        if (await context.Customers.AnyAsync())
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
            Address = c.Address
        }));

        await context.SaveChangesAsync();

        logger.LogInformation("{Count} clients de demonstration crees.", DemoData.Customers.Length);
    }
}
