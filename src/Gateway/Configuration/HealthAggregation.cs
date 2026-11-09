using System.Text.Json;
using GestionCommandes.BuildingBlocks.Http;
using Microsoft.Extensions.Options;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>
/// Agregation de l'etat de sante de tous les microservices, exposee par la
/// gateway sur <c>/health/services</c>.
///
/// Utile a la demonstration et au script <c>infra/07-monitoring.ps1</c> :
/// un seul appel depuis la machine cliente suffit a voir l'etat de la
/// plateforme, alors qu'aucun service n'est publie sur l'hote.
/// </summary>
public static class HealthAggregation
{
    /// <summary>Nom du HttpClient utilise pour interroger les services.</summary>
    public const string HttpClientName = "health-probe";

    /// <summary>Expose l'endpoint d'agregation.</summary>
    public static void MapServicesHealth(this WebApplication app)
    {
        app.MapGet("/health/services", async (
                IHttpClientFactory factory,
                IOptions<ServiceEndpointsOptions> endpoints,
                IOptions<GatewayOptions> gateway,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("HealthAggregation");
                var client = factory.CreateClient(HttpClientName);

                var targets = new Dictionary<string, string>
                {
                    ["identity-service"] = endpoints.Value.Identity,
                    ["catalog-service"] = endpoints.Value.Catalog,
                    ["inventory-service"] = endpoints.Value.Inventory,
                    ["order-service"] = endpoints.Value.Order,
                    ["delivery-service"] = endpoints.Value.Delivery,
                    ["notification-service"] = endpoints.Value.Notification,
                    ["web-client"] = gateway.Value.WebClientUrl
                };

                var probes = await Task.WhenAll(targets.Select(target =>
                    ProbeAsync(client, target.Key, target.Value, logger, cancellationToken)));

                var healthy = probes.Count(p => p.Healthy);

                var payload = new
                {
                    status = healthy == probes.Length ? "Healthy" : healthy == 0 ? "Unhealthy" : "Degraded",
                    healthy,
                    total = probes.Length,
                    checkedAt = DateTimeOffset.UtcNow,
                    services = probes
                };

                return Results.Json(
                    payload,
                    new JsonSerializerOptions { WriteIndented = true },
                    statusCode: healthy == probes.Length
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status503ServiceUnavailable);
            })
            .AllowAnonymous()
            .WithName("AgregationSante")
            .WithSummary("Etat de sante consolide de tous les microservices.");
    }

    private static async Task<ProbeResult> ProbeAsync(
        HttpClient client,
        string name,
        string baseAddress,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var url = baseAddress.TrimEnd('/') + "/health/ready";
        var started = DateTimeOffset.UtcNow;

        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            var elapsed = (DateTimeOffset.UtcNow - started).TotalMilliseconds;

            return new ProbeResult(
                name,
                baseAddress,
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                Math.Round(elapsed, 1),
                null);
        }
        catch (Exception ex)
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalMilliseconds;

            logger.LogWarning("Sonde de sante en echec pour {Service} ({Url}) : {Message}", name, url, ex.Message);

            return new ProbeResult(name, baseAddress, false, null, Math.Round(elapsed, 1), ex.Message);
        }
    }

    /// <summary>Resultat d'une sonde de sante.</summary>
    private sealed record ProbeResult(
        string Service,
        string Address,
        bool Healthy,
        int? StatusCode,
        double ElapsedMs,
        string? Error);
}
