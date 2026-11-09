using System.Text.Json.Serialization;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace GestionCommandes.Shared.Hosting;

/// <summary>
/// Socle applique a chaque service : erreurs JSON, Swagger, sondes de sante,
/// adresses des autres services. Un Program.cs de service tient ainsi en une
/// vingtaine de lignes lisibles.
/// </summary>
public static class ServiceSetup
{
    /// <summary>Enregistre le socle commun et renvoie l'emplacement de la base.</summary>
    /// <param name="builder">Builder de l'application.</param>
    /// <param name="serviceName">Nom technique, par exemple <c>catalog-service</c>.</param>
    /// <param name="description">Description affichee dans Swagger.</param>
    public static StoragePaths AddServiceDefaults(
        this WebApplicationBuilder builder,
        string serviceName,
        string description)
    {
        var paths = StoragePaths.Resolve(builder.Configuration, builder.Environment, serviceName);

        builder.Services.AddSingleton(paths);

        // Journalisation sur la sortie standard : lisible avec « docker compose logs ».
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        // Adresses des autres services (noms DNS Docker en conteneur, localhost en dev).
        builder.Services.AddOptions<ServiceEndpointsOptions>()
            .Bind(builder.Configuration.GetSection(ServiceEndpointsOptions.SectionName));

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddHealthChecks();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = $"Gestion des commandes - {serviceName}",
                Version = "v1",
                Description = description
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Jeton obtenu par POST /api/auth/login. Saisir uniquement le jeton, " +
                    "le prefixe Bearer est ajoute automatiquement."
            });

            // Microsoft.OpenApi 2.x : la reference se declare via OpenApiSecuritySchemeReference.
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
            });
        });

        return paths;
    }

    /// <summary>
    /// Enregistre les controleurs : enums en texte lisible et proprietes nulles omises.
    /// </summary>
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services
            .AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

        return services;
    }

    /// <summary>Ajoute la verification de la base a la sonde <c>/health/ready</c>.</summary>
    public static IServiceCollection AddDatabaseCheck<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck<TContext>>("database", tags: ["ready"]);

        return services;
    }

    /// <summary>
    /// Branche le pipeline commun, dans l'ordre : erreurs, Swagger, authentification,
    /// puis les sondes de sante.
    /// </summary>
    /// <param name="app">Application.</param>
    /// <param name="serviceName">Nom technique du service.</param>
    /// <param name="includeSwagger">Faux pour la gateway, qui expose sa propre interface.</param>
    public static WebApplication UseServiceDefaults(
        this WebApplication app,
        string serviceName,
        bool includeSwagger = true)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (includeSwagger)
        {
            app.UseSwagger(options => options.RouteTemplate = "swagger/{documentName}/swagger.json");
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", $"{serviceName} v1");
                options.RoutePrefix = "swagger";
                options.DocumentTitle = $"{serviceName} - API";
                options.DocExpansion(DocExpansion.List);
            });
        }

        app.UseAuthentication();
        app.UseAuthorization();

        // /health/live : le processus repond. /health/ready : la base est accessible.
        // Les deux sont anonymes pour que Docker puisse les interroger sans jeton.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = (context, report) => WriteHealthAsync(context, report, serviceName)
        }).AllowAnonymous();

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = (context, report) => WriteHealthAsync(context, report, serviceName)
        }).AllowAnonymous();

        return app;
    }

    private static Task WriteHealthAsync(HttpContext context, HealthReport report, string serviceName)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(new
        {
            service = serviceName,
            status = report.Status.ToString(),
            machine = Environment.MachineName,
            timestamp = DateTimeOffset.UtcNow,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                error = entry.Value.Exception?.Message
            })
        });
    }
}

/// <summary>Verifie que la base SQLite du service est joignable.</summary>
/// <typeparam name="TContext">DbContext du service.</typeparam>
public sealed class DatabaseHealthCheck<TContext>(TContext context) : IHealthCheck
    where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Base accessible.")
                : HealthCheckResult.Unhealthy("Base inaccessible.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Echec de connexion a la base.", ex);
        }
    }
}
