using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Services;
using GestionCommandes.Shared.Hosting;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------------------
// order-service : clients, commandes et cycle de vie des commandes.
// C'est le service qui PILOTE : il appelle catalog-service (stock et prix) et
// delivery-service (bordereau de livraison).
// Port dans le conteneur : 8080. En local (dotnet run) : 5003.
// ---------------------------------------------------------------------------

const string ServiceName = "order-service";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Commandes : clients, brouillons, confirmation, expedition et annulation.");

builder.Services.AddDbContext<OrderDbContext>(options => options.UseSqlite(storage.ConnectionString));
builder.Services.AddDatabaseCheck<OrderDbContext>();

builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddApiControllers();

// --- Clients HTTP vers les autres services ---------------------------------
// La cle interne est ajoutee automatiquement a chaque appel sortant : c'est ce
// qui authentifie order-service aupres des autres conteneurs.
builder.Services.AddTransient<InternalKeyHeaderHandler>();

builder.Services.AddHttpClient<CatalogClient>((provider, client) =>
    {
        var endpoints = provider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;

        client.BaseAddress = new Uri(endpoints.Catalog.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddHttpMessageHandler<InternalKeyHeaderHandler>();

builder.Services.AddHttpClient<DeliveryClient>((provider, client) =>
    {
        var endpoints = provider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;

        client.BaseAddress = new Uri(endpoints.Delivery.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(15);
    })
    .AddHttpMessageHandler<InternalKeyHeaderHandler>();

builder.Services.AddScoped<OrderWorkflow>();

var app = builder.Build();

// Cree la base et les clients de demonstration au premier demarrage.
await OrderDbInitializer.InitializeAsync(app.Services);

app.UseServiceDefaults(ServiceName);
app.MapControllers();

app.Run();
