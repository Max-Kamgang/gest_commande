using GestionCommandes.BuildingBlocks.Correlation;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Health;
using GestionCommandes.BuildingBlocks.Http;
using GestionCommandes.BuildingBlocks.Observability;
using GestionCommandes.BuildingBlocks.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace GestionCommandes.BuildingBlocks.Hosting;

/// <summary>
/// Socle commun applique a chaque microservice : logs, ProblemDetails, Correlation-ID,
/// Swagger, health checks, options d'adressage inter-services.
/// L'objectif est qu'un <c>Program.cs</c> de service reste court et lisible.
/// </summary>
public static class ServiceDefaults
{
    /// <summary>Enregistre le socle commun et renvoie les chemins de stockage resolus.</summary>
    /// <param name="builder">Builder de l'application.</param>
    /// <param name="serviceName">Nom technique du service (ex. <c>catalog-service</c>).</param>
    /// <param name="description">Description affichee dans Swagger.</param>
    public static StoragePaths AddServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        string description)
    {
        // 1. Dossiers de donnees / logs (et verification d'ecriture sous ContainerUser).
        var paths = StorageLocator.Resolve(builder.Configuration, builder.Environment, serviceName);

        // 2. Logs structures Serilog (console + fichier dans le volume).
        builder.AddSerilogLogging(serviceName, paths);

        var services = builder.Services;

        services.AddSingleton(paths);
        services.AddSingleton(new ServiceIdentity(serviceName));
        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();

        // 3. Adresses des autres services (DNS Docker en conteneur, localhost en dev).
        services.AddOptions<ServiceEndpointsOptions>()
            .Bind(builder.Configuration.GetSection(ServiceEndpointsOptions.SectionName));

        // 4. Erreurs au format RFC 9457, enrichies du Correlation-ID.
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance ??=
                    $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

                context.ProblemDetails.Extensions["service"] = serviceName;
                context.ProblemDetails.Extensions["correlationId"] =
                    context.HttpContext.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
                        ? raw as string
                        : context.HttpContext.TraceIdentifier;
            };
        });
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // 5. OpenAPI + schema JWT.
        services.AddSwaggerWithJwt(serviceName, description);

        // 6. Health checks (les sondes « ready » sont ajoutees par chaque service).
        services.AddHealthChecks();

        return paths;
    }

    /// <summary>Ajoute la sonde <c>/health/ready</c> verifiant la base du service.</summary>
    public static IServiceCollection AddDatabaseReadyCheck<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddHealthChecks()
            .AddCheck<DbContextHealthCheck<TContext>>(
                name: "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthEndpointExtensions.ReadyTag]);

        return services;
    }

    /// <summary>
    /// Active la publication d'evenements vers notification-service
    /// (file en memoire + service de fond + client HTTP resilient).
    /// </summary>
    public static IServiceCollection AddEventPublishing(this IServiceCollection services)
    {
        services.AddSingleton<EventDispatchQueue>();
        services.AddSingleton<IEventPublisher, QueuedEventPublisher>();
        services.AddTransient<CorrelationIdPropagationHandler>();

        services.AddHttpClient(EventDispatcherService.HttpClientName, (provider, client) =>
            {
                var endpoints = provider
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<ServiceEndpointsOptions>>().Value;

                client.BaseAddress = new Uri(endpoints.Notification.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddStandardResilienceHandler(ResilientHttpClientExtensions.ConfigureFireAndForgetPipeline);

        services.AddHostedService<EventDispatcherService>();

        return services;
    }

    /// <summary>
    /// Branche le pipeline commun. L'ordre est important :
    /// Correlation-ID d'abord (pour que tout le reste en dispose), puis le gestionnaire
    /// d'exceptions, puis la journalisation des requetes.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app, string serviceName)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();

        // Transforme les codes d'erreur sans corps (404 de routage, 405...) en ProblemDetails.
        app.UseStatusCodePages();

        app.UseRequestLogging();

        app.UseSwaggerWithJwt(serviceName);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapServiceHealthChecks(serviceName);

        return app;
    }
}
