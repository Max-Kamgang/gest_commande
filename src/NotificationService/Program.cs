using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.NotificationService.Data;
using GestionCommandes.NotificationService.Services;
using Microsoft.EntityFrameworkCore;

// ---------------------------------------------------------------------------
// notification-service : reception des evenements d'integration, stockage et
// diffusion SIMULEE (email / SMS ecrits dans le dossier outbox du volume).
// Port interne en conteneur : 8080. Port local (dotnet run) : 5006.
// ---------------------------------------------------------------------------

const string ServiceName = "notification-service";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    ServiceName,
    "Notifications : reception des evenements (commande confirmee, stock bas, " +
    "livraison effectuee), historisation et envoi simule d'email et de SMS.");

builder.Services.AddDbContext<NotificationDbContext>((provider, options) =>
    options.UseSqlite(provider.GetRequiredService<StoragePaths>().SqliteConnectionString));

builder.Services.AddDatabaseReadyCheck<NotificationDbContext>();
builder.Services.AddPlatformAuthentication(builder.Configuration);

// notification-service est le consommateur final : il ne publie pas d'evenement.
builder.Services.AddSingleton<INotificationSender, SimulatedNotificationSender>();
builder.Services.AddScoped<NotificationManager>();

builder.Services.AddApiControllers(ServiceName);

var app = builder.Build();

await NotificationDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
