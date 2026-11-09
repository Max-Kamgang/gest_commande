using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GestionCommandes.BuildingBlocks.Health;

/// <summary>
/// Sonde de disponibilite de la base SQLite du service : utilisee par <c>/health/ready</c>.
/// Une simple connexion suffit a prouver que le volume est monte et le fichier accessible.
/// </summary>
/// <typeparam name="TContext">DbContext du service.</typeparam>
public sealed class DbContextHealthCheck<TContext>(TContext context) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context_,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var canConnect = await context.Database.CanConnectAsync(cancellationToken);
            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                ["provider"] = context.Database.ProviderName ?? "inconnu",
                ["elapsedMs"] = stopwatch.ElapsedMilliseconds
            };

            return canConnect
                ? HealthCheckResult.Healthy("Base de donnees accessible.", data)
                : HealthCheckResult.Unhealthy("Base de donnees injoignable.", data: data);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return HealthCheckResult.Unhealthy(
                "Erreur lors de l'acces a la base de donnees.",
                ex,
                new Dictionary<string, object> { ["elapsedMs"] = stopwatch.ElapsedMilliseconds });
        }
    }
}
