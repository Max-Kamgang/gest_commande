using GestionCommandes.BuildingBlocks.Data;
using GestionCommandes.InventoryService.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.InventoryService.Data;

/// <summary>Base SQLite propre a inventory-service (<c>inventory-service.db</c>).</summary>
public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : ServiceDbContext(options)
{
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<StockItem> StockItems => Set<StockItem>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<StockReservation> Reservations => Set<StockReservation>();

    public DbSet<StockReservationLine> ReservationLines => Set<StockReservationLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Warehouse>(entity =>
        {
            entity.ToTable("Warehouses");
            entity.HasKey(w => w.Id);
            entity.Property(w => w.Code).IsRequired();
            entity.Property(w => w.Name).IsRequired();
            entity.Property(w => w.City).IsRequired();
            entity.HasIndex(w => w.Code).IsUnique();

            entity.HasMany(w => w.StockItems)
                .WithOne(s => s.Warehouse!)
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockItem>(entity =>
        {
            entity.ToTable("StockItems");
            entity.HasKey(s => s.Id);

            // Un seul enregistrement de stock par couple (produit, entrepot).
            entity.HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
            entity.HasIndex(s => s.ProductId);

            // Verrou optimiste : SQLite n'a pas de rowversion natif, on utilise un GUID.
            entity.Property(s => s.RowVersion)
                .IsConcurrencyToken()
                .IsRequired();

            // Proprietes calculees : non persistees.
            entity.Ignore(s => s.QuantityAvailable);
            entity.Ignore(s => s.IsLowStock);

            entity.ToTable(table =>
            {
                // Garde-fous au niveau base : le stock et les reservations restent coherents
                // meme si un bug applicatif passait a travers les verifications metier.
                table.HasCheckConstraint("CK_StockItems_OnHand_Positif", "QuantityOnHand >= 0");
                table.HasCheckConstraint("CK_StockItems_Reserved_Positif", "QuantityReserved >= 0");
                table.HasCheckConstraint(
                    "CK_StockItems_Reserved_Max",
                    "QuantityReserved <= QuantityOnHand");
            });
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements");
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(m => m.PerformedBy).IsRequired();

            entity.HasOne(m => m.StockItem)
                .WithMany()
                .HasForeignKey(m => m.StockItemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(m => m.OccurredAt);
            entity.HasIndex(m => new { m.ProductId, m.OccurredAt });
            entity.HasIndex(m => m.Reference);
        });

        modelBuilder.Entity<StockReservation>(entity =>
        {
            entity.ToTable("Reservations");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Reference).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // La reference metier (numero de commande) est la cle d'idempotence.
            entity.HasIndex(r => r.Reference).IsUnique();
            entity.HasIndex(r => r.Status);

            entity.HasMany(r => r.Lines)
                .WithOne(l => l.Reservation!)
                .HasForeignKey(l => l.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockReservationLine>(entity =>
        {
            entity.ToTable("ReservationLines");
            entity.HasKey(l => l.Id);
            entity.HasIndex(l => new { l.ReservationId, l.ProductId, l.WarehouseId });
        });
    }

    /// <summary>
    /// Regenere le jeton de concurrence des lignes de stock modifiees.
    /// C'est ce qui rend le verrou optimiste operationnel sous SQLite.
    /// </summary>
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
        foreach (var entry in ChangeTracker.Entries<StockItem>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
                // La valeur d'origine sert a la clause WHERE : on ne change que la nouvelle.
                entry.Entity.RowVersion = Guid.NewGuid();
            }
        }
    }
}
