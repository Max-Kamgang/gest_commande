using System.Text.Json;
using System.Text.Json.Serialization;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCommandes.BuildingBlocks.Hosting;

/// <summary>Configuration commune des controleurs d'API (JSON, validation, negociation).</summary>
public static class ApiControllerDefaults
{
    private static readonly JsonSerializerOptions ProblemJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Enregistre les controleurs avec :
    /// <list type="bullet">
    ///   <item>enums serialises en chaines lisibles,</item>
    ///   <item>proprietes nulles omises,</item>
    ///   <item>erreurs de validation renvoyees en ProblemDetails enrichi (400).</item>
    /// </list>
    /// </summary>
    public static IMvcBuilder AddApiControllers(this IServiceCollection services, string serviceName)
    {
        var mvc = services
            .AddControllers(options =>
            {
                options.ReturnHttpNotAcceptable = true;
                options.SuppressAsyncSuffixInActionNames = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(context.ModelState)
                {
                    Type = "https://microservices.micro.local/errors/validation",
                    Title = "Donnees invalides",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "Une ou plusieurs valeurs fournies sont invalides. Voir le detail dans 'errors'.",
                    Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}"
                };

                problem.Extensions["errorCode"] = "VALIDATION_FAILED";
                problem.Extensions["service"] = serviceName;
                problem.Extensions["correlationId"] =
                    context.HttpContext.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
                        ? raw as string
                        : context.HttpContext.TraceIdentifier;

                // Ecrit explicitement le type application/problem+json (RFC 9457) : la
                // negociation de contenu choisirait sinon le formateur JSON classique.
                return new ContentResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentType = "application/problem+json; charset=utf-8",
                    Content = JsonSerializer.Serialize(problem, ProblemJsonOptions)
                };
            };
        });

        return mvc;
    }
}
