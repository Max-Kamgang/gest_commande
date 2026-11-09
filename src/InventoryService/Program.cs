using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// inventory-service : entrepots, stock, mouvements et reservations.
// Participant de la saga de commande (reserver / liberer / consommer).
// Port interne en conteneur : 8080. Port local (dotnet run) : 5003.
// ---------------------------------------------------------------------------

const string ServiceName = "inventory-service";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    ServiceName,
    "Gestion du stock : entrepots, niveaux par produit, mouvements, reservations " +
    "et alertes de stock bas. Verrou optimiste contre la survente.");

builder.Services.AddDbContext<InventoryDbContext>((provider, options) =>
    options.UseSqlite(provider.GetRequiredService<StoragePaths>().SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<InventoryDbContext>();
builder.Services.AddPlatformAuthentication(builder.Configuration);

// Diffusion des evenements « stock bas » vers notification-service.
builder.Services.AddEventPublishing();

builder.Services.AddScoped<StockLedger>();
builder.Services.AddScoped<WarehouseManager>();
builder.Services.AddScoped<StockManager>();
builder.Services.AddScoped<ReservationManager>();

builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

await InventoryDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();

/// <summary>Point d’entrée visible pour WebApplicationFactory dans les tests d’intégration.</summary>
public partial class Program;
