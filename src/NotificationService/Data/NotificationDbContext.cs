using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.NotificationService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.NotificationService.Data;

/// <summary>Base SQLite propre a notification-service (<c>notification-service.db</c>).</summary>
public class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.EventType).IsRequired();
            entity.Property(n => n.Source).IsRequired();
            entity.Property(n => n.Title).IsRequired();
            entity.Property(n => n.Message).IsRequired();
            entity.Property(n => n.Severity).IsRequired();
            entity.Property(n => n.Channel).IsRequired();
            entity.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Idempotence : un evenement rejoue n'est stocke qu'une fois.
            entity.HasIndex(n => n.EventId).IsUnique();
            entity.HasIndex(n => n.ReceivedAt);
            entity.HasIndex(n => new { n.EventType, n.ReceivedAt });
            entity.HasIndex(n => n.Severity);
            entity.HasIndex(n => n.Status);
            entity.HasIndex(n => n.Subject);
        });
    }
}
