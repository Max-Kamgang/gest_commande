using GestionCommandes.Gateway.Configuration;
using GestionCommandes.Shared.Hosting;
using GestionCommandes.Shared.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.SwaggerUI;
using System.Threading.RateLimiting;

// ---------------------------------------------------------------------------
// gateway : point d'entree UNIQUE de la plateforme.
//
//   - sert l'interface web (fichiers statiques de wwwroot),
//   - route /api/... vers les quatre microservices (YARP),
//   - valide le jeton JWT des la frontiere,
//   - supprime l'en-tete de cle interne venant de l'exterieur,
//   - limite le debit par client,
//   - expose une interface Swagger unique et /health/services.
//
// Comme l'interface web est servie ici, tous les appels du navigateur sont de
// MEME ORIGINE : il n'y a aucun CORS a configurer.
//
// Seul conteneur publie sur l'hote : ports 80 et 443.
// En local (dotnet run) : http://localhost:5000.
// ---------------------------------------------------------------------------

const string ServiceName = "gateway";

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    ServiceName,
    "Point d'entree unique : interface web, routage, authentification et supervision.");

var endpoints = builder.Configuration.GetSection(ServiceEndpointsOptions.SectionName)
                    .Get<ServiceEndpointsOptions>()
                ?? new ServiceEndpointsOptions();

// --- Authentification ------------------------------------------------------
builder.Services.AddPlatformAuthentication(builder.Configuration);

// La gateway n'accepte QUE le jeton JWT d'un utilisateur : la cle interne ne
// doit jamais suffire a franchir la frontiere.
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build());

// --- Limitation de debit ---------------------------------------------------
// Une fenetre fixe par adresse IP : protege la connexion contre les tentatives
// en rafale sans penaliser l'usage normal de l'interface.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "inconnu",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Gateway:RequestsPerMinute", 300),
                Window = TimeSpan.FromMinutes(1)
            }));
});

// --- Reverse proxy ---------------------------------------------------------
builder.Services
    .AddReverseProxy()
    .LoadFromMemory([.. GatewayRoutes.BuildRoutes()], [.. GatewayRoutes.BuildClusters(endpoints)])
    .AddTransforms(GatewayRoutes.ApplyGlobalTransforms);

builder.Services.AddHttpClient(HealthAggregation.HttpClientName, client =>
    client.Timeout = TimeSpan.FromSeconds(5));

var app = builder.Build();

// Socle commun : erreurs JSON, authentification, /health/live et /health/ready.
// La gateway n'a pas de base de donnees : aucune sonde « ready » applicative.
app.UseServiceDefaults(ServiceName, includeSwagger: false);

app.UseRateLimiter();

// Interface web : fichiers statiques de wwwroot, index.html par defaut.
app.UseDefaultFiles();
app.UseStaticFiles();

// Interface Swagger unique : un selecteur liste les quatre API, servies via les
// routes d'agregation /openapi/<service>/v1.json.
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Gestion des commandes - toutes les API";
    options.DocExpansion(DocExpansion.None);

    options.SwaggerEndpoint("/openapi/identity/v1.json", "identity-service");
    options.SwaggerEndpoint("/openapi/catalog/v1.json", "catalog-service");
    options.SwaggerEndpoint("/openapi/order/v1.json", "order-service");
    options.SwaggerEndpoint("/openapi/delivery/v1.json", "delivery-service");
});

app.MapServicesHealth();

// Petit annuaire lisible, pratique pendant la demonstration.
app.MapGet("/api", () => Results.Json(new
    {
        plateforme = "Gestion des commandes, du stock et des livraisons",
        documentation = "/swagger",
        sante = new[] { "/health/live", "/health/services" },
        api = new
        {
            connexion = "POST /api/auth/login",
            comptes = "/api/users",
            categories = "/api/categories",
            produits = "/api/products",
            stock = "/api/stock",
            clients = "/api/customers",
            commandes = "/api/orders",
            livreurs = "/api/couriers",
            livraisons = "/api/deliveries",
            notifications = "/api/notifications"
        }
    }))
    .AllowAnonymous();

app.MapReverseProxy();

// Toute autre adresse renvoie l'interface web : la navigation cote client
// fonctionne meme si l'utilisateur recharge la page sur /commandes.
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();
