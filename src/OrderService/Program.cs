using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Http;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// order-service : clients, commandes, machine a etats et ORCHESTRATION de la saga.
// Appelle catalog-service (prix), inventory-service (stock) et delivery-service.
// Port interne en conteneur : 8080. Port local (dotnet run) : 5004.
// ---------------------------------------------------------------------------

const string ServiceName = "order-service";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    ServiceName,
    "Commandes clients : saisie, machine a etats (Brouillon, Confirmee, EnPreparation, " +
    "Expediee, Livree, Annulee) et orchestration de la saga de confirmation avec compensation.");

builder.Services.AddDbContext<OrderDbContext>((provider, options) =>
    options.UseSqlite(provider.GetRequiredService<StoragePaths>().SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<OrderDbContext>();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddEventPublishing();

// Journal d'idempotence (en-tete Idempotency-Key sur la creation et la confirmation).
builder.Services.AddScoped<IIdempotencyStore, EfIdempotencyStore<OrderDbContext>>();

// --- Clients HTTP typés vers les services participants ---------------------
// Resolution par nom DNS Docker en conteneur (http://catalog-service:8080),
// par localhost en developpement. Pipeline de resilience : timeout par tentative,
// rejeu avec backoff exponentiel + jitter, coupe-circuit.
builder.Services.AddResilientServiceClient<ICatalogClient, CatalogClient>(
    endpoints => endpoints.Catalog,
    ServiceName);

builder.Services.AddResilientServiceClient<IInventoryClient, InventoryClient>(
    endpoints => endpoints.Inventory,
    ServiceName);

builder.Services.AddResilientServiceClient<IDeliveryClient, DeliveryClient>(
    endpoints => endpoints.Delivery,
    ServiceName);

builder.Services.AddScoped<CustomerManager>();
builder.Services.AddScoped<OrderManager>();
builder.Services.AddScoped<OrderSaga>();

builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

await OrderDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();

/// <summary>Point d’entrée visible pour WebApplicationFactory dans les tests d’intégration.</summary>
public partial class Program;
