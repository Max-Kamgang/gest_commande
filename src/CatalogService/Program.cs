using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.CatalogService.Data;
using GestionCommandes.CatalogService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// catalog-service : categories, produits, prix et activation.
// Port interne en conteneur : 8080. Port local (dotnet run) : 5002.
// ---------------------------------------------------------------------------

const string ServiceName = "catalog-service";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Catalogue produits : categories, produits, prix, activation et tarification pour la saga de commande.");

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlite(storage.SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<CatalogDbContext>();
builder.Services.AddPlatformAuthentication(builder.Configuration);

builder.Services.AddScoped<CatalogManager>();
builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

await CatalogDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
