using GestionCommandes.BuildingBlocks.Correlation;
using GestionCommandes.BuildingBlocks.Health;
using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;

// ---------------------------------------------------------------------------
// web-client : interface web (HTML / CSS / JavaScript, sans framework lourd)
// servie par ASP.NET Core.
//
// Elle ne consomme QUE la gateway : aucune adresse de microservice n'apparait
// dans le code JavaScript. L'adresse de l'API est fournie au navigateur par
// /app-config.json, ce qui permet au meme code de fonctionner :
//   - servi par la gateway (apiBaseUrl vide, appels relatifs, meme origine),
//   - en developpement local sur le port 5010 (apiBaseUrl = http://localhost:5000).
//
// Port interne en conteneur : 8080. Port local (dotnet run) : 5010.
// ---------------------------------------------------------------------------

const string ServiceName = "web-client";

var builder = WebApplication.CreateBuilder(args);

var storage = builder.AddServiceDefaults(
    ServiceName,
    "Interface web de la plateforme. Consomme exclusivement la gateway.");

var apiBaseUrl = builder.Configuration["WebClient:ApiBaseUrl"] ?? string.Empty;
var appTitle = builder.Configuration["WebClient:Title"]
               ?? "Gestion des commandes, du stock et des livraisons";

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseRequestLogging();

// Fichiers statiques : index.html par defaut, pas de cache sur le HTML pour que
// la demonstration reflete toujours la derniere version deployee.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.File.Name;

        context.Context.Response.Headers.CacheControl =
            path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache, no-store, must-revalidate"
                : "public, max-age=300";
    }
});

app.MapServiceHealthChecks(ServiceName);

/// Configuration transmise au navigateur au demarrage de l'application web.
app.MapGet("/app-config.json", () => Results.Json(new
{
    apiBaseUrl,
    title = appTitle,
    builtWith = ".NET 10 / ASP.NET Core",
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"
}));

// Application monopage : toute route inconnue renvoie index.html.
app.MapFallbackToFile("index.html");

app.Run();
