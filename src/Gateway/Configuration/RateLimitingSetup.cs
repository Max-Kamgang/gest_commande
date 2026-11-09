using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>
/// Limitation de debit de la gateway.
///
/// Deux politiques : une generale (fenetre glissante par client) et une
/// renforcee sur la connexion, qui limite les tentatives de force brute. Le
/// partitionnement se fait par utilisateur authentifie quand c'est possible,
/// sinon par adresse IP.
/// </summary>
public static class RateLimitingSetup
{
    /// <summary>Enregistre les politiques de limitation de debit.</summary>
    public static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(GatewayRoutes.GeneralRateLimit, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;

                return RateLimitPartition.GetSlidingWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = settings.RequestsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = settings.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.AddPolicy(GatewayRoutes.LoginRateLimit, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<GatewayOptions>>().Value;

                return RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.LoginAttemptsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            // Reponse 429 au format ProblemDetails, avec l'en-tete Retry-After.
            options.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value)
                    ? (int)value.TotalSeconds
                    : 60;

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfter.ToString(CultureInfo.InvariantCulture);

                var correlationId =
                    context.HttpContext.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
                        ? raw as string
                        : context.HttpContext.TraceIdentifier;

                var payload = new Dictionary<string, object?>
                {
                    ["type"] = "https://microservices.micro.local/errors/rate_limited",
                    ["title"] = "Trop de requetes",
                    ["status"] = StatusCodes.Status429TooManyRequests,
                    ["detail"] =
                        $"Le quota de requetes est depasse. Reessayez dans {retryAfter} seconde(s).",
                    ["instance"] =
                        $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}",
                    ["errorCode"] = "RATE_LIMITED",
                    ["service"] = "gateway",
                    ["retryAfterSeconds"] = retryAfter,
                    ["correlationId"] = correlationId
                };

                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(payload),
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Clef de partition : l'identifiant d'utilisateur si la requete est
    /// authentifiee, sinon l'adresse IP du client.
    /// </summary>
    private static string ResolvePartitionKey(HttpContext httpContext)
    {
        var user = httpContext.User.FindFirst("sub")?.Value
                   ?? httpContext.User.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(user))
        {
            return $"user:{user}";
        }

        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "inconnu";

        return $"ip:{ip}";
    }
}
