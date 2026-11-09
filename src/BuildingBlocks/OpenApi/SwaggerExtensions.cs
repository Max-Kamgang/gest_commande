using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace GestionCommandes.BuildingBlocks.OpenApi;

/// <summary>Configuration Swagger / OpenAPI identique dans tous les services, avec le schema JWT Bearer.</summary>
public static class SwaggerExtensions
{
    /// <summary>Enregistre le generateur OpenAPI et declare l'authentification Bearer.</summary>
    public static IServiceCollection AddSwaggerWithJwt(
        this IServiceCollection services,
        string serviceName,
        string description)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = $"Gestion des commandes - {serviceName}",
                Version = "v1",
                Description = description,
                Contact = new OpenApiContact { Name = "Projet microservices - SRV-MICRO-01" }
            });

            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description =
                    "Jeton JWT obtenu via POST /api/auth/login sur identity-service. " +
                    "Saisir uniquement le jeton (le prefixe « Bearer » est ajoute automatiquement)."
            };

            options.AddSecurityDefinition("Bearer", scheme);

            // Microsoft.OpenApi 2.x : la reference se declare via OpenApiSecuritySchemeReference.
            options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>()
            });

            // Inclut les commentaires XML des controleurs et DTO.
            foreach (var xml in Directory.GetFiles(AppContext.BaseDirectory, "*.xml"))
            {
                try
                {
                    options.IncludeXmlComments(xml, includeControllerXmlComments: true);
                }
                catch (Exception)
                {
                    // Fichier XML non lie a la documentation : on l'ignore silencieusement.
                }
            }
        });

        return services;
    }

    /// <summary>Expose <c>/swagger</c> et <c>/swagger/v1/swagger.json</c>.</summary>
    public static WebApplication UseSwaggerWithJwt(this WebApplication app, string serviceName)
    {
        app.UseSwagger(options => options.RouteTemplate = "swagger/{documentName}/swagger.json");
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", $"{serviceName} v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = $"{serviceName} - API";
            options.DocExpansion(DocExpansion.List);
            options.DisplayRequestDuration();
        });

        return app;
    }
}
