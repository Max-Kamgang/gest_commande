using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Domain;
using GestionCommandes.InventoryService.Dtos;
using GestionCommandes.InventoryService.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCommandes.UnitTests;

/// <summary>
/// Reservations de stock : atomicite, repartition multi-entrepots, idempotence,
/// compensation (liberation) et consommation a l'expedition.
/// </summary>
public class ReservationManagerTests : IDisposable
{
    private static readonly Guid Produit = Guid.Parse("22222222-0000-0000-0000-00000000000a");
    private static readonly Guid Autre = Guid.Parse("22222222-0000-0000-0000-00000000000b");
    private static readonly Guid Douala = Guid.Parse("11111111-0000-0000-0000-00000000000a");
    private static readonly Guid Yaounde = Guid.Parse("11111111-0000-0000-0000-00000000000b");

    private readonly SqliteConnection _connection = TestDb.OpenInMemory();
    private readonly InventoryDbContext _db;
    private readonly FakeEvents _events = new();
    private readonly ReservationManager _reservations;

    public ReservationManagerTests()
    {
        _db = TestDb.Inventory(_connection);

        var douala = new Warehouse { Id = Douala, Code = "DLA-01", Name = "Douala", City = "Douala" };
        var yaounde = new Warehouse { Id = Yaounde, Code = "YAO-01", Name = "Yaounde", City = "Yaounde" };
        _db.Warehouses.AddRange(douala, yaounde);

        _db.StockItems.Add(new StockItem { ProductId = Produit, WarehouseId = Douala, QuantityOnHand = 25, LowStockThreshold = 5, Warehouse = douala });
        _db.StockItems.Add(new StockItem { ProductId = Produit, WarehouseId = Yaounde, QuantityOnHand = 8, LowStockThreshold = 3, Warehouse = yaounde });
        _db.StockItems.Add(new StockItem { ProductId = Autre, WarehouseId = Douala, QuantityOnHand = 4, LowStockThreshold = 1, Warehouse = douala });
        _db.SaveChanges();

        var user = new FakeUser();
        var ledger = new StockLedger(_db, user, _events, NullLogger<StockLedger>.Instance);
        _reservations = new ReservationManager(_db, ledger, user, NullLogger<ReservationManager>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static ReservationRequest Demande(string reference, params (Guid Produit, int Quantite)[] lignes) => new()
    {
        Reference = reference,
        Lines = lignes.Select(l => new ReservationLineRequest { ProductId = l.Produit, Quantity = l.Quantite }).ToList()
    };

    private async Task<StockItem> LireAsync(Guid produit, Guid entrepot) =>
        await _db.StockItems.AsNoTracking().SingleAsync(s => s.ProductId == produit && s.WarehouseId == entrepot);

    [Fact]
    public async Task Une_demande_est_servie_par_les_entrepots_les_mieux_fournis_en_premier()
    {
        var (reservation, existed) = await _reservations.ReserveAsync(Demande("CMD-1", (Produit, 30)), default);

        Assert.False(existed);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(25, reservation.Lines.Single(l => l.WarehouseId == Douala).Quantity);
        Assert.Equal(5, reservation.Lines.Single(l => l.WarehouseId == Yaounde).Quantity);

        Assert.Equal(25, (await LireAsync(Produit, Douala)).QuantityReserved);
        Assert.Equal(5, (await LireAsync(Produit, Yaounde)).QuantityReserved);
    }

    [Fact]
    public async Task Une_reservation_est_atomique_si_une_seule_ligne_manque()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _reservations.ReserveAsync(Demande("CMD-2", (Autre, 2), (Produit, 999)), default));

        Assert.Equal("INSUFFICIENT_STOCK", ex.ErrorCode);
        Assert.True(ex.Extensions.ContainsKey("shortages"));
        Assert.Equal(0, (await LireAsync(Autre, Douala)).QuantityReserved);
        Assert.Equal(0, (await LireAsync(Produit, Douala)).QuantityReserved);
    }

    [Fact]
    public async Task Rejouer_la_meme_reference_ne_double_jamais_la_reservation()
    {
        await _reservations.ReserveAsync(Demande("CMD-3", (Produit, 4)), default);
        var (_, existed) = await _reservations.ReserveAsync(Demande("CMD-3", (Produit, 4)), default);

        Assert.True(existed);
        Assert.Equal(4, (await LireAsync(Produit, Douala)).QuantityReserved);
    }

    [Fact]
    public async Task La_liberation_rend_le_stock_disponible_et_est_idempotente()
    {
        await _reservations.ReserveAsync(Demande("CMD-4", (Produit, 10)), default);

        var liberee = await _reservations.ReleaseAsync("CMD-4", "Compensation", default);
        var deuxieme = await _reservations.ReleaseAsync("CMD-4", "Compensation", default);

        Assert.Equal(ReservationStatus.Liberee, liberee.Status);
        Assert.Equal(ReservationStatus.Liberee, deuxieme.Status);
        Assert.Equal(0, (await LireAsync(Produit, Douala)).QuantityReserved);
        Assert.Equal(25, (await LireAsync(Produit, Douala)).QuantityAvailable);
    }

    [Fact]
    public async Task La_consommation_transforme_la_reservation_en_sortie_definitive()
    {
        await _reservations.ReserveAsync(Demande("CMD-5", (Produit, 10)), default);

        var consommee = await _reservations.CommitAsync("CMD-5", "Expedition", default);

        Assert.Equal(ReservationStatus.Consommee, consommee.Status);
        var douala = await LireAsync(Produit, Douala);
        Assert.Equal(15, douala.QuantityOnHand);
        Assert.Equal(0, douala.QuantityReserved);
    }

    [Fact]
    public async Task Une_reservation_consommee_ne_peut_plus_etre_liberee()
    {
        await _reservations.ReserveAsync(Demande("CMD-6", (Produit, 3)), default);
        await _reservations.CommitAsync("CMD-6", null, default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _reservations.ReleaseAsync("CMD-6", null, default));

        Assert.Equal("RESERVATION_ALREADY_CONSUMED", ex.ErrorCode);
    }

    [Fact]
    public async Task Une_reservation_liberee_peut_etre_reposee()
    {
        await _reservations.ReserveAsync(Demande("CMD-7", (Produit, 5)), default);
        await _reservations.ReleaseAsync("CMD-7", null, default);

        var (reservation, existed) = await _reservations.ReserveAsync(Demande("CMD-7", (Produit, 5)), default);

        Assert.False(existed);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(5, (await LireAsync(Produit, Douala)).QuantityReserved);
    }

    [Fact]
    public async Task Passer_sous_le_seuil_publie_une_alerte_de_stock_bas()
    {
        await _reservations.ReserveAsync(Demande("CMD-8", (Produit, 30)), default);

        Assert.Contains(_events.Published, e => e.EventType == EventTypes.StockLow);
    }
}
