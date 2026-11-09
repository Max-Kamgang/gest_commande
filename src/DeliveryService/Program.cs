using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Services;
using GestionCommandes.Shared.Hosting;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------------------
// delivery-service : livreurs, bordereaux de livraison et notifications de suivi.
// Port dans le conteneur : 8080. En local (dotnet run) : 5004.
// ---------------------------------------------------------------------------

const string ServiceName = "delivery-service";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Livraisons : livreurs, bordereaux, suivi des tournees et notifications.");

builder.Services.AddDbContext<DeliveryDbContext>(options => options.UseSqlite(storage.ConnectionString));
builder.Services.AddDatabaseCheck<DeliveryDbContext>();

builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddApiControllers();

// Appel sortant unique : prevenir order-service qu'une commande est livree.
builder.Services.AddTransient<InternalKeyHeaderHandler>();

builder.Services.AddHttpClient<OrderClient>((provider, client) =>
    {
        var endpoints = provider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;

        client.BaseAddress = new Uri(endpoints.Order.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddHttpMessageHandler<InternalKeyHeaderHandler>();

builder.Services.AddScoped<DeliveryWorkflow>();

var app = builder.Build();

// Cree la base et les livreurs de demonstration au premier demarrage.
await DeliveryDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
