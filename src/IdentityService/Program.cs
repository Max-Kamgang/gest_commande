using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Data;
using GestionCommandes.IdentityService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// identity-service : comptes utilisateurs, connexion et emission des jetons JWT.
// Port interne en conteneur : 8080. Port local (dotnet run) : 5001.
// ---------------------------------------------------------------------------

const string ServiceName = "identity-service";

var builder = WebApplication.CreateBuilder(args);

// Socle commun : Serilog, ProblemDetails, Correlation-ID, Swagger, health checks.
var storage = builder.AddServiceDefaults(
    ServiceName,
    "Gestion des comptes, authentification et emission des jetons JWT (roles Admin, Gestionnaire, Livreur, Client).");

// Base SQLite dediee a ce service, dans le volume monte.
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlite(storage.SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<IdentityDbContext>();

// identity-service emet les jetons ET valide ceux qu'il recoit (endpoints /me, /api/users).
builder.Services.AddPlatformAuthentication(builder.Configuration);

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<UserService>();

builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

// Migrations + donnees de demonstration.
await IdentityDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
