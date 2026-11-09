using GestionCommandes.CatalogService.Data;
using GestionCommandes.Shared.Hosting;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// catalog-service : categories, produits, prix ET stock.
// Port dans le conteneur : 8080. En local (dotnet run) : 5002.
// ---------------------------------------------------------------------------

const string ServiceName = "catalog-service";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Catalogue et stock : categories, produits, prix, niveaux, mouvements et reservations.");

builder.Services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(storage.ConnectionString));
builder.Services.AddDatabaseCheck<CatalogDbContext>();

builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddApiControllers();

var app = builder.Build();

// Cree la base et le catalogue de demonstration au premier demarrage.
await CatalogDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
