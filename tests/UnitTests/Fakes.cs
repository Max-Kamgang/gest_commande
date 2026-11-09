using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Events;
using Rol = GestionCommandes.BuildingBlocks.Security.Roles;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.UnitTests;

/// <summary>Utilisateur simule, dont le role est fixe par le test.</summary>
public sealed class FakeUser : ICurrentUser
{
    public bool IsAuthenticated => true;
    public Guid? UserId { get; init; } = Guid.NewGuid();
    public string? UserName { get; init; } = "test@micro.local";
    public Guid? CourierId { get; init; }
    public Guid? CustomerId { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = [Rol.Gestionnaire];
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    public bool IsBackOffice => IsInRole(Rol.Admin) || IsInRole(Rol.Gestionnaire);
}

/// <summary>Collecte les evenements publies, sans file ni HTTP.</summary>
public sealed class FakeEvents : IEventPublisher
{
    public List<IntegrationEvent> Published { get; } = [];

    public bool Publish(IntegrationEvent integrationEvent)
    {
        Published.Add(integrationEvent);
        return true;
    }
}

/// <summary>Idempotence en memoire.</summary>
public sealed class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<(string, string), IdempotencyRecord> _records = new();

    public Task<IdempotencyRecord?> FindAsync(string operation, string key, CancellationToken cancellationToken) =>
        Task.FromResult(_records.TryGetValue((operation, key), out var record) ? record : null);

    public Task SaveAsync(string operation, string key, int statusCode, string responseBody, string? resourceId,
        CancellationToken cancellationToken)
    {
        _records[(operation, key)] = new IdempotencyRecord
        {
            Operation = operation,
            Key = key,
            StatusCode = statusCode,
            ResponseBody = responseBody,
            ResourceId = resourceId
        };
        return Task.CompletedTask;
    }
}

/// <summary>Catalogue simule : prix connus par produit.</summary>
public sealed class FakeCatalog : ICatalogClient
{
    public Dictionary<Guid, (string Sku, string Name, decimal Price)> Prices { get; } = new();

    public Task<PricingResponseDto> GetPricingAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        var response = new PricingResponseDto();

        foreach (var id in ids)
        {
            if (Prices.TryGetValue(id, out var price))
            {
                response.Items.Add(new PricingItemDto
                {
                    ProductId = id, Sku = price.Sku, Name = price.Name, UnitPrice = price.Price, IsActive = true
                });
            }
            else
            {
                response.Missing.Add(id);
            }
        }

        return Task.FromResult(response);
    }
}

/// <summary>Inventaire simule : enregistre les appels et peut echouer a volonte.</summary>
public sealed class FakeInventory : IInventoryClient
{
    public List<ReservationRequestDto> ReserveCalls { get; } = [];
    public List<string> ReleaseCalls { get; } = [];
    public List<string> CommitCalls { get; } = [];
    public Exception? ReserveFailure { get; set; }

    public Task<ReservationResultDto> ReserveAsync(ReservationRequestDto request, CancellationToken cancellationToken)
    {
        ReserveCalls.Add(request);

        if (ReserveFailure is not null)
        {
            throw ReserveFailure;
        }

        return Task.FromResult(new ReservationResultDto { Reference = request.Reference, Status = "Active" });
    }

    public Task<ReservationResultDto> ReleaseAsync(string reference, string reason, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add(reference);
        return Task.FromResult(new ReservationResultDto { Reference = reference, Status = "Liberee" });
    }

    public Task<ReservationResultDto> CommitAsync(string reference, string reason, CancellationToken cancellationToken)
    {
        CommitCalls.Add(reference);
        return Task.FromResult(new ReservationResultDto { Reference = reference, Status = "Consommee" });
    }
}

/// <summary>Livraison simulee.</summary>
public sealed class FakeDelivery : IDeliveryClient
{
    public Task<DeliveryResultDto> CreateDeliveryAsync(CreateDeliveryRequestDto request, CancellationToken cancellationToken) =>
        Task.FromResult(new DeliveryResultDto { Id = Guid.NewGuid(), Reference = "LIV-TEST-00001", OrderNumber = request.OrderNumber });
}

/// <summary>Fabrique des bases SQLite en memoire, schema cree a la volee.</summary>
public static class TestDb
{
    public static SqliteConnection OpenInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    public static OrderDbContext Orders(SqliteConnection connection)
    {
        var context = new OrderDbContext(new DbContextOptionsBuilder<OrderDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        return context;
    }

    public static InventoryDbContext Inventory(SqliteConnection connection)
    {
        var context = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(connection).Options);
        context.Database.EnsureCreated();
        return context;
    }
}
