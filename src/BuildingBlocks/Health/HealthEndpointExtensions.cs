using System.Text.Json;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionCommandes.BuildingBlocks.Health;

/// <summary>Expose <c>/health/live</c> et <c>/health/ready</c> avec une reponse JSON lisible.</summary>
public static class HealthEndpointExtensions
{
    /// <summary>Tag applique aux sondes incluses dans <c>/health/ready</c>.</summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// <c>/health/live</c> : le processus repond (aucune dependance testee).
    /// <c>/health/ready</c> : le service peut servir du trafic (base accessible).
    /// Les deux endpoints sont anonymes, afin que Docker et les scripts de supervision
    /// puissent les interroger sans jeton.
    /// </summary>
    public static IEndpointRouteBuilder MapServiceHealthChecks(
        this IEndpointRouteBuilder endpoints,
        string serviceName)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            // Aucune sonde : on verifie uniquement que le pipeline HTTP repond.
            Predicate = _ => false,
            ResponseWriter = (context, report) => WriteResponseAsync(context, report, serviceName, "live")
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = (context, report) => WriteResponseAsync(context, report, serviceName, "ready")
        }).AllowAnonymous();

        return endpoints;
    }

    private static async Task WriteResponseAsync(
        HttpContext context,
        HealthReport report,
        string serviceName,
        string probe)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var correlationId = context.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
            ? raw as string
            : context.TraceIdentifier;

        var payload = new
        {
            service = serviceName,
            probe,
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            version = typeof(HealthEndpointExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            machine = Environment.MachineName,
            timestamp = DateTimeOffset.UtcNow,
            correlationId,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                error = entry.Value.Exception?.Message
            })
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
