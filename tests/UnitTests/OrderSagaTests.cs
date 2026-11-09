using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using GestionCommandes.OrderService.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionCommandes.UnitTests;

/// <summary>
/// Saga de confirmation : figement des prix, reservation, compensation en cas
/// d'echec, et garde-fous de la machine a etats.
/// </summary>
public class OrderSagaTests : IDisposable
{
    private static readonly Guid Client = Guid.Parse("33333333-0000-0000-0000-00000000000a");
    private static readonly Guid Ordinateur = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Cable = Guid.Parse("22222222-0000-0000-0000-000000000006");

    private readonly SqliteConnection _connection = TestDb.OpenInMemory();
    private readonly OrderDbContext _db;
    private readonly FakeCatalog _catalog = new();
    private readonly FakeInventory _inventory = new();
    private readonly FakeEvents _events = new();
    private readonly FakeUser _user = new();
    private readonly OrderManager _orders;
    private readonly OrderSaga _saga;

    public OrderSagaTests()
    {
        _db = TestDb.Orders(_connection);
        _db.Customers.Add(new Customer { Id = Client, Code = "CLI-T", Name = "Client test", Email = "t@micro.local", City = "Douala", IsActive = true });
        _db.SaveChanges();

        _catalog.Prices[Ordinateur] = ("INFO-PC-001", "Ordinateur portable", 450_000m);
        _catalog.Prices[Cable] = ("RES-CAB-006", "Cable reseau", 3_500m);

        var idempotency = new MemoryIdempotencyStore();
        _orders = new OrderManager(_db, idempotency, _user, NullLogger<OrderManager>.Instance);
        _saga = new OrderSaga(_db, _orders, _catalog, _inventory, new FakeDelivery(), idempotency, _user, _events,
            NullLogger<OrderSaga>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<OrderResponse> BrouillonAsync(params (Guid Produit, int Quantite)[] lignes)
    {
        var (order, _) = await _orders.CreateAsync(new CreateOrderRequest
        {
            CustomerId = Client,
            Lines = lignes.Select(l => new CreateOrderLineRequest { ProductId = l.Produit, Quantity = l.Quantite }).ToList()
        }, null, default);

        return order;
    }

    [Fact]
    public async Task La_confirmation_fige_les_prix_et_reserve_le_stock()
    {
        var brouillon = await BrouillonAsync((Ordinateur, 2), (Cable, 3));

        var (confirmee, replayed) = await _saga.ConfirmAsync(brouillon.Id, null, default);

        Assert.False(replayed);
        Assert.Equal(OrderStatus.Confirmee, confirmee.Status);
        Assert.Equal(450_000m, confirmee.Lines.Single(l => l.ProductId == Ordinateur).UnitPrice);
        Assert.Equal(900_000m + 10_500m, confirmee.SubTotal);
        Assert.Single(_inventory.ReserveCalls);
        Assert.Equal(brouillon.Number, _inventory.ReserveCalls[0].Reference);
        Assert.Contains(_events.Published, e => e.EventType == EventTypes.OrderConfirmed);
    }

    [Fact]
    public async Task Si_inventory_est_indisponible_la_commande_reste_en_brouillon_et_la_reservation_est_compensee()
    {
        _inventory.ReserveFailure = new ServiceUnavailableException("inventory-service", "Le service 'inventory-service' est injoignable.");
        var brouillon = await BrouillonAsync((Ordinateur, 1));

        await Assert.ThrowsAsync<ServiceUnavailableException>(() => _saga.ConfirmAsync(brouillon.Id, null, default));

        var relue = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == brouillon.Id);
        Assert.Equal(OrderStatus.Brouillon, relue.Status);
        Assert.NotNull(relue.LastError);
        Assert.Contains("inventory-service", relue.LastError);
        Assert.Contains(brouillon.Number, _inventory.ReleaseCalls);
    }

    [Fact]
    public async Task Un_produit_inconnu_du_catalogue_arrete_la_saga_avant_toute_reservation()
    {
        var inconnu = Guid.Parse("22222222-0000-0000-0000-0000000000ff");
        var brouillon = await BrouillonAsync((Ordinateur, 1), (inconnu, 1));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _saga.ConfirmAsync(brouillon.Id, null, default));

        Assert.Equal("UNKNOWN_PRODUCT", ex.ErrorCode);
        Assert.Empty(_inventory.ReserveCalls);
        var relue = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == brouillon.Id);
        Assert.Equal(OrderStatus.Brouillon, relue.Status);
    }

    [Fact]
    public async Task Un_stock_insuffisant_renvoie_un_conflit_et_laisse_la_commande_en_brouillon()
    {
        _inventory.ReserveFailure = new ConflictException("Stock insuffisant", "INSUFFICIENT_STOCK");
        var brouillon = await BrouillonAsync((Ordinateur, 500));

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _saga.ConfirmAsync(brouillon.Id, null, default));

        Assert.Equal("INSUFFICIENT_STOCK", ex.ErrorCode);
        var relue = await _db.Orders.AsNoTracking().SingleAsync(o => o.Id == brouillon.Id);
        Assert.Equal(OrderStatus.Brouillon, relue.Status);
    }

    [Fact]
    public async Task Confirmer_deux_fois_est_refuse_par_la_machine_a_etats()
    {
        var brouillon = await BrouillonAsync((Cable, 1));
        await _saga.ConfirmAsync(brouillon.Id, null, default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _saga.ConfirmAsync(brouillon.Id, null, default));

        Assert.Equal("INVALID_STATE_TRANSITION", ex.ErrorCode);
    }

    [Fact]
    public async Task Annuler_une_commande_confirmee_libere_le_stock()
    {
        var brouillon = await BrouillonAsync((Cable, 4));
        await _saga.ConfirmAsync(brouillon.Id, null, default);

        var annulee = await _saga.CancelAsync(brouillon.Id, "Demande du client", default);

        Assert.Equal(OrderStatus.Annulee, annulee.Status);
        Assert.Contains(brouillon.Number, _inventory.ReleaseCalls);
        Assert.Contains(_events.Published, e => e.EventType == EventTypes.OrderCancelled);
    }

    [Fact]
    public async Task Une_commande_expediee_ne_peut_plus_etre_annulee()
    {
        var brouillon = await BrouillonAsync((Cable, 1));
        await _saga.ConfirmAsync(brouillon.Id, null, default);
        await _saga.PrepareAsync(brouillon.Id, default);
        await _saga.ShipAsync(brouillon.Id, default);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _saga.CancelAsync(brouillon.Id, "Trop tard", default));

        Assert.Equal("INVALID_STATE_TRANSITION", ex.ErrorCode);
        Assert.Contains(brouillon.Number, _inventory.CommitCalls);
    }
}
