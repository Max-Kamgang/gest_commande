using GestionCommandes.IdentityService.Data;
using GestionCommandes.IdentityService.Services;
using GestionCommandes.Shared.Hosting;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// identity-service : comptes, roles et emission des jetons JWT.
// Port dans le conteneur : 8080. En local (dotnet run) : 5001.
// ---------------------------------------------------------------------------

const string ServiceName = "identity-service";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Comptes utilisateurs, roles et emission des jetons JWT de la plateforme.");

builder.Services.AddDbContext<IdentityDbContext>(options => options.UseSqlite(storage.ConnectionString));
builder.Services.AddDatabaseCheck<IdentityDbContext>();

builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddScoped<TokenService>();
builder.Services.AddApiControllers();

var app = builder.Build();

// Cree la base et les comptes de demonstration au premier demarrage.
await IdentityDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
