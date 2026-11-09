using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Http;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.DeliveryService.Clients;
using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// delivery-service : livreurs, livraisons, affectation et suivi.
// Informe order-service lorsqu'une livraison est effectuee.
// Port interne en conteneur : 8080. Port local (dotnet run) : 5005.
// ---------------------------------------------------------------------------

const string ServiceName = "delivery-service";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    ServiceName,
    "Livraisons : bordereaux crees a l'expedition, affectation des livreurs, " +
    "suivi des statuts et historique. Un livreur ne met a jour que ses livraisons.");

builder.Services.AddDbContext<DeliveryDbContext>((provider, options) =>
    options.UseSqlite(provider.GetRequiredService<StoragePaths>().SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<DeliveryDbContext>();
builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddEventPublishing();

// Rappel vers order-service a la cloture d'une livraison.
builder.Services.AddResilientServiceClient<IOrderClient, OrderClient>(
    endpoints => endpoints.Order,
    ServiceName);

builder.Services.AddScoped<CourierManager>();
builder.Services.AddScoped<DeliveryManager>();

builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

await DeliveryDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
