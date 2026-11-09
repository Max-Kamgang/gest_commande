using GestionCommandes.BuildingBlocks.Correlation;
using GestionCommandes.BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;

namespace GestionCommandes.BuildingBlocks.Http;

/// <summary>
/// Enregistrement des HttpClient typés inter-services avec un pipeline de resilience
/// standard (timeout par tentative, rejeu avec backoff exponentiel + jitter,
/// coupe-circuit) et propagation automatique du Correlation-ID.
/// </summary>
public static class ResilientHttpClientExtensions
{
    /// <summary>
    /// Declare un client typé pointant vers un autre microservice.
    /// </summary>
    /// <typeparam name="TClient">Interface du client.</typeparam>
    /// <typeparam name="TImplementation">Implementation concrete.</typeparam>
    /// <param name="services">Conteneur de services.</param>
    /// <param name="baseAddressSelector">Selectionne l'URL du service cible dans la configuration.</param>
    /// <param name="userAgent">Nom du service appelant, envoye en User-Agent.</param>
    public static IHttpClientBuilder AddResilientServiceClient<TClient, TImplementation>(
        this IServiceCollection services,
        Func<ServiceEndpointsOptions, string> baseAddressSelector,
        string userAgent)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddTransient<CorrelationIdPropagationHandler>();
        services.AddTransient<InternalKeyPropagationHandler>();

        var builder = services
            .AddHttpClient<TClient, TImplementation>((provider, client) =>
            {
                var endpoints = provider.GetRequiredService<IOptions<ServiceEndpointsOptions>>().Value;
                client.BaseAddress = new Uri(baseAddressSelector(endpoints).TrimEnd('/') + "/");
                client.DefaultRequestHeaders.UserAgent.ParseAdd($"{userAgent}/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                // Le timeout reel est pilote par le pipeline de resilience ci-dessous.
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddHttpMessageHandler<CorrelationIdPropagationHandler>()
            .AddHttpMessageHandler<InternalKeyPropagationHandler>();

        builder.AddStandardResilienceHandler(ConfigureStandardPipeline);

        return builder;
    }

    /// <summary>Pipeline par defaut : 10 s par tentative, 2 rejeux, coupe-circuit 15 s.</summary>
    public static void ConfigureStandardPipeline(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions options)
    {
        // Duree maximale d'une tentative unitaire.
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);

        // Duree maximale de l'appel complet (toutes tentatives comprises).
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(35);

        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromMilliseconds(400);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
        options.Retry.UseJitter = true;

        // Contrainte du framework : SamplingDuration >= 2 x AttemptTimeout.
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    }

    /// <summary>
    /// Pipeline allege pour les appels non bloquants (diffusion d'evenements) :
    /// on echoue vite plutot que de retenir un thread de fond.
    /// </summary>
    public static void ConfigureFireAndForgetPipeline(Microsoft.Extensions.Http.Resilience.HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(12);

        options.Retry.MaxRetryAttempts = 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(300);
        options.Retry.BackoffType = DelayBackoffType.Constant;
        options.Retry.UseJitter = true;

        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(20);
        options.CircuitBreaker.FailureRatio = 0.6;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(20);
    }
}
