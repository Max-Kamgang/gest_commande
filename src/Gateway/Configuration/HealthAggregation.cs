using GestionCommandes.Shared.Security;
using Microsoft.Extensions.Options;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>
/// Expose <c>/health/services</c> : l'etat des quatre microservices en une seule
/// requete. Pratique pour la demonstration et pour la supervision, car aucun
/// service n'est publie sur l'hote : seule la gateway peut les interroger.
/// </summary>
public static class HealthAggregation
{
    /// <summary>Nom du client HTTP des sondes.</summary>
    public const string HttpClientName = "health-probe";

    /// <summary>Branche l'endpoint d'agregation.</summary>
    /// <param name="app">Application.</param>
    public static WebApplication MapServicesHealth(this WebApplication app)
    {
        app.MapGet("/health/services", async (
                IHttpClientFactory factory,
                IOptions<ServiceEndpointsOptions> options,
                CancellationToken cancellationToken) =>
            {
                var endpoints = options.Value;

                var targets = new Dictionary<string, string>
                {
                    ["identity-service"] = endpoints.Identity,
                    ["catalog-service"] = endpoints.Catalog,
                    ["order-service"] = endpoints.Order,
                    ["delivery-service"] = endpoints.Delivery
                };

                var client = factory.CreateClient(HttpClientName);

                // Les quatre sondes partent en parallele : la reponse arrive en
                // une seule attente, pas en quatre.
                var probes = await Task.WhenAll(targets.Select(async target =>
                {
                    try
                    {
                        var response = await client.GetAsync(
                            $"{target.Value.TrimEnd('/')}/health/ready", cancellationToken);

                        return new
                        {
                            service = target.Key,
                            statut = response.IsSuccessStatusCode ? "Healthy" : "Unhealthy",
                            code = (int)response.StatusCode,
                            detail = (string?)null
                        };
                    }
                    catch (Exception ex)
                    {
                        return new
                        {
                            service = target.Key,
                            statut = "Unreachable",
                            code = 0,
                            detail = (string?)ex.Message
                        };
                    }
                }));

                var global = probes.All(probe => probe.statut == "Healthy") ? "Healthy" : "Degraded";

                return Results.Json(new
                {
                    plateforme = "gestion-commandes",
                    statut = global,
                    horodatage = DateTimeOffset.UtcNow,
                    services = probes
                });
            })
            .AllowAnonymous()
            .WithName("SanteDesServices");

        return app;
    }
}
