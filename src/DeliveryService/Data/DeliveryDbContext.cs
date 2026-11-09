using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.Shared.Data;
using GestionCommandes.Shared.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Data;

/// <summary>Base SQLite de delivery-service (<c>delivery-service.db</c>).</summary>
public class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : ServiceDbContext(options)
{
    /// <summary>Livreurs.</summary>
    public DbSet<Courier> Couriers => Set<Courier>();

    /// <summary>Bordereaux de livraison.</summary>
    public DbSet<Delivery> Deliveries => Set<Delivery>();

    /// <summary>Lignes des bordereaux.</summary>
    public DbSet<DeliveryLine> DeliveryLines => Set<DeliveryLine>();

    /// <summary>Suivi des livraisons.</summary>
    public DbSet<DeliveryTrackingEntry> Tracking => Set<DeliveryTrackingEntry>();

    /// <summary>Notifications de suivi.</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>Compteurs de numerotation.</summary>
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
            entity.HasIndex(c => c.Code).IsUnique();

            entity.HasMany(c => c.Deliveries)
                .WithOne(d => d.Courier!)
                .HasForeignKey(d => d.CourierId)
                // SetNull : supprimer un livreur ne supprime pas ses bordereaux.
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Delivery>(entity =>
        {
            entity.ToTable("Deliveries");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Reference).IsRequired();
            entity.HasIndex(d => d.Reference).IsUnique();
            // Numero de commande unique : garantit qu'un rappel ne cree pas de doublon.
            entity.HasIndex(d => d.OrderNumber).IsUnique();
            entity.HasIndex(d => d.Status);

            entity.Property(d => d.RowVersion).IsConcurrencyToken();

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
        });

        modelBuilder.Entity<DeliveryTrackingEntry>(entity =>
        {
            entity.ToTable("DeliveryTracking");
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => new { t.DeliveryId, t.ChangedAt });
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(n => n.Id);
            entity.HasIndex(n => n.CreatedAt);
            entity.HasIndex(n => n.IsRead);
        });

        modelBuilder.Entity<DeliverySequence>(entity =>
        {
            entity.ToTable("DeliverySequences");
            entity.HasKey(s => s.Year);
        });
    }

    /// <summary>Regenere le jeton de concurrence de chaque bordereau modifie.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Delivery>().Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.RowVersion = Guid.NewGuid();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Cree la base si besoin et insere les livreurs de demonstration.</summary>
public static class DeliveryDbInitializer
{
    /// <summary>Prepare la base du service.</summary>
    /// <param name="services">Fournisseur de services de l'application.</param>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<DeliveryDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DeliveryDbContext>>();

        await context.Database.EnsureCreatedAsync();

        if (await context.Couriers.AnyAsync())
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
            Zone = c.Zone
        }));

        await context.SaveChangesAsync();

        logger.LogInformation("{Count} livreurs de demonstration crees.", DemoData.Couriers.Length);
    }
}
