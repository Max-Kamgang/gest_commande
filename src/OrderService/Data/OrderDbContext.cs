using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.OrderService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Data;

/// <summary>Base SQLite propre a order-service (<c>order-service.db</c>).</summary>
public class OrderDbContext(DbContextOptions<OrderDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    public DbSet<OrderStatusHistoryEntry> OrderHistory => Set<OrderStatusHistoryEntry>();

    public DbSet<OrderSequence> OrderSequences => Set<OrderSequence>();

    /// <summary>Journal d'idempotence des POST sensibles (creation et confirmation).</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).IsRequired();
            entity.Property(c => c.Name).IsRequired();
            entity.Property(c => c.Email).IsRequired();
            entity.Property(c => c.City).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();
            entity.HasIndex(c => c.Email);

            entity.HasMany(c => c.Orders)
                .WithOne(o => o.Customer!)
                .HasForeignKey(o => o.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Number).IsRequired();
            entity.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(o => o.Currency).IsRequired();

            entity.Property(o => o.SubTotal).HasMoneyConversion();
            entity.Property(o => o.VatAmount).HasMoneyConversion();
            entity.Property(o => o.Total).HasMoneyConversion();
            // Le taux de TVA est un ratio : stocke en 1/10 000 pour rester exact.
            entity.Property(o => o.VatRate).HasConversion(
                rate => (long)Math.Round(rate * 10_000m, MidpointRounding.AwayFromZero),
                stored => stored / 10_000m);

            entity.Property(o => o.RowVersion).IsConcurrencyToken().IsRequired();

            entity.HasIndex(o => o.Number).IsUnique();
            entity.HasIndex(o => o.Status);
            entity.HasIndex(o => new { o.CustomerId, o.CreatedAt });

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
            entity.Property(l => l.UnitPrice).HasMoneyConversion();
            entity.Property(l => l.LineTotal).HasMoneyConversion();
            entity.HasIndex(l => new { l.OrderId, l.ProductId }).IsUnique();

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_OrderLines_Quantity_Positif",
                "Quantity > 0"));
        });

        modelBuilder.Entity<OrderStatusHistoryEntry>(entity =>
        {
            entity.ToTable("OrderHistory");
            entity.HasKey(h => h.Id);
            entity.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(h => h.ChangedBy).IsRequired();
            entity.HasIndex(h => new { h.OrderId, h.ChangedAt });
        });

        modelBuilder.Entity<OrderSequence>(entity =>
        {
            entity.ToTable("OrderSequences");
            entity.HasKey(s => s.Year);
        });

        modelBuilder.ApplyIdempotencyConfiguration();
    }

    /// <summary>Regenere le jeton de concurrence des commandes modifiees.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        BumpRowVersions();

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override int SaveChanges()
    {
        BumpRowVersions();

        return base.SaveChanges();
    }

    private void BumpRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries<Order>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid();
            }
        }
    }
}
