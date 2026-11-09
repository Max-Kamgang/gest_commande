using GestionCommandes.BuildingBlocks.Correlation;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Http;
using GestionCommandes.BuildingBlocks.Observability;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.Gateway.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerUI;

// ---------------------------------------------------------------------------
// gateway : point d'entree UNIQUE de la plateforme (YARP).
//
// Responsabilites :
//   - routage vers les six microservices et vers le client web,
//   - validation du jeton JWT des la frontiere,
//   - CORS, limitation de debit, propagation du Correlation-ID,
//   - suppression de l'en-tete de cle interne venant de l'exterieur,
//   - interface Swagger unique agregeant les six API,
//   - agregation de l'etat de sante sur /health/services.
//
// Seule la gateway est publiee sur l'hote : ports 80 et 443 en conteneur,
// 5000 (HTTP) et 5443 (HTTPS) en developpement local.
// ---------------------------------------------------------------------------

const string ServiceName = "gateway";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Point d'entree unique : routage, authentification, CORS, limitation de debit " +
    "et tracabilite. Agregation des six API dans une seule interface Swagger.");

builder.Services.AddOptions<GatewayOptions>()
    .Bind(builder.Configuration.GetSection(GatewayOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var gatewayOptions = builder.Configuration.GetSection(GatewayOptions.SectionName).Get<GatewayOptions>()
                     ?? new GatewayOptions();

var serviceEndpoints = builder.Configuration.GetSection(ServiceEndpointsOptions.SectionName)
                           .Get<ServiceEndpointsOptions>()
                       ?? new ServiceEndpointsOptions();

// --- Authentification / autorisation ---------------------------------------
builder.Services.AddPlatformAuthentication(builder.Configuration);

builder.Services.AddAuthorizationBuilder()
    // La gateway n'accepte QUE le jeton JWT utilisateur : la cle interne ne doit jamais
    // suffire a franchir la frontiere (elle est reservee aux appels entre services).
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());
    // Les routes marquees « anonymous » (valeur reservee de YARP) ne demandent aucun jeton.
    // Les services appliquent de toute facon leurs propres regles (defense en profondeur).

// --- CORS -------------------------------------------------------------------
builder.Services.AddCors(options => options.AddPolicy(
    GatewayRoutes.CorsPolicy,
    policy => policy
        .WithOrigins(gatewayOptions.AllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationConstants.HeaderName, "Idempotent-Replay", "Retry-After")
        .SetPreflightMaxAge(TimeSpan.FromHours(1))));

// --- Limitation de debit ----------------------------------------------------
builder.Services.AddGatewayRateLimiting();

// --- Reverse proxy YARP -----------------------------------------------------
builder.Services
    .AddReverseProxy()
    .LoadFromMemory(
        [.. GatewayRoutes.BuildRoutes()],
        [.. GatewayRoutes.BuildClusters(serviceEndpoints, gatewayOptions.WebClientUrl)])
    .AddTransforms(GatewayRoutes.ApplyGlobalTransforms);

// --- Client HTTP des sondes de sante ---------------------------------------
builder.Services.AddHttpClient(HealthAggregation.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("gateway-health-probe/1.0");
});

var app = builder.Build();

// Le socle commun branche Correlation-ID, journalisation, ProblemDetails,
// authentification et les endpoints /health/live et /health/ready.
// CORS AVANT l'authentification : les requetes de pre-vol (OPTIONS) n'portent pas de
// jeton et doivent recevoir leurs en-tetes CORS sans etre rejetees en 401.
app.UseCors(GatewayRoutes.CorsPolicy);

app.UseServiceDefaults(ServiceName, includeSwagger: false);

app.UseRateLimiter();

// Interface Swagger unique : un selecteur liste les six API, servies au travers
// des routes d'agregation /openapi/<service>/v1.json.
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Gestion des commandes - API (toutes les API)";
    options.DocExpansion(DocExpansion.None);
    options.DisplayRequestDuration();

    options.SwaggerEndpoint("/openapi/identity/v1.json", "identity-service");
    options.SwaggerEndpoint("/openapi/catalog/v1.json", "catalog-service");
    options.SwaggerEndpoint("/openapi/inventory/v1.json", "inventory-service");
    options.SwaggerEndpoint("/openapi/order/v1.json", "order-service");
    options.SwaggerEndpoint("/openapi/delivery/v1.json", "delivery-service");
    options.SwaggerEndpoint("/openapi/notification/v1.json", "notification-service");
});

app.MapServicesHealth();

// Petit annuaire lisible, pratique pendant la demonstration.
app.MapGet("/api", (IOptions<GatewayOptions> options) => Results.Ok(new
    {
        plateforme = "Gestion des commandes, du stock et des livraisons",
        gateway = "point d'entree unique",
        documentation = "/swagger",
        sante = new[] { "/health/live", "/health/ready", "/health/services" },
        api = new
        {
            authentification = "/api/auth/login",
            utilisateurs = "/api/users",
            categories = "/api/categories",
            produits = "/api/products",
            entrepots = "/api/warehouses",
            stock = "/api/stock",
            reservations = "/api/reservations",
            clients = "/api/customers",
            commandes = "/api/orders",
            livreurs = "/api/couriers",
            livraisons = "/api/deliveries",
            notifications = "/api/notifications"
        },
        clientWeb = options.Value.WebClientUrl
    }))
    .AllowAnonymous()
    .WithName("Annuaire");

app.MapReverseProxy();

app.Run();
