using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.DeliveryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Data;

/// <summary>Base SQLite propre a delivery-service (<c>delivery-service.db</c>).</summary>
public class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Courier> Couriers => Set<Courier>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();

    public DbSet<DeliveryTrackingEntry> Tracking => Set<DeliveryTrackingEntry>();

    public DbSet<DeliverySequence> DeliverySequences => Set<DeliverySequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Courier>(entity =>
        {
            entity.ToTable("Couriers");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Code).IsRequired();
            entity.Property(c => c.FullName).IsRequired();
            entity.Property(c => c.Email).IsRequired();
            entity.Property(c => c.Vehicle).IsRequired();
            entity.HasIndex(c => c.Code).IsUnique();
            entity.HasIndex(c => c.Email).IsUnique();

            entity.HasMany(c => c.Deliveries)
                .WithOne(d => d.Courier!)
                .HasForeignKey(d => d.CourierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.ToTable("Deliveries");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Reference).IsRequired();
            entity.Property(d => d.OrderNumber).IsRequired();
            entity.Property(d => d.CustomerName).IsRequired();
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(d => d.RowVersion).IsConcurrencyToken().IsRequired();

            entity.HasIndex(d => d.Reference).IsUnique();
            // Une commande ne donne qu'un bordereau : la creation est idempotente.
            entity.HasIndex(d => d.OrderNumber).IsUnique();
            entity.HasIndex(d => d.Status);
            entity.HasIndex(d => new { d.CourierId, d.Status });

            entity.HasMany(d => d.Lines)
                .WithOne(l => l.Delivery!)
                .HasForeignKey(l => l.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(d => d.Tracking)
                .WithOne(t => t.Delivery!)
                .HasForeignKey(t => t.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryLine>(entity =>
        {
            entity.ToTable("DeliveryLines");
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.DeliveryId, l.ProductId });
        });

        modelBuilder.Entity<DeliveryTrackingEntry>(entity =>
        {
            entity.ToTable("DeliveryTracking");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.FromStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(t => t.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(t => t.ChangedBy).IsRequired();
            entity.HasIndex(t => new { t.DeliveryId, t.ChangedAt });
        });

        modelBuilder.Entity<DeliverySequence>(entity =>
        {
            entity.ToTable("DeliverySequences");
            entity.HasKey(s => s.Year);
        });
    }

    /// <inheritdoc />
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
        foreach (var entry in ChangeTracker.Entries<Delivery>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid();
            }
        }
    }
}
