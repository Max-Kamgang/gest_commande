using System.Net;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace GestionCommandes.BuildingBlocks.Errors;

/// <summary>
/// Gestionnaire d'exceptions global. Toute exception non interceptee devient une
/// reponse <c>application/problem+json</c> conforme a la RFC 9457, enrichie du
/// Correlation-ID. Les details techniques restent dans les logs.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
            ? raw as string ?? httpContext.TraceIdentifier
            : httpContext.TraceIdentifier;

        ProblemDetails problem;

        switch (exception)
        {
            case AppException app:
                // Erreur attendue : journalisee en Warning, pas en Error.
                logger.LogWarning(
                    app is ServiceUnavailableException ? (app as ServiceUnavailableException)!.InnerCause : null,
                    "Erreur applicative {ErrorCode} ({StatusCode}) sur {Method} {Path} : {Detail}",
                    app.ErrorCode,
                    (int)app.StatusCode,
                    httpContext.Request.Method,
                    httpContext.Request.Path,
                    app.Message);

                problem = new ProblemDetails
                {
                    Status = (int)app.StatusCode,
                    Title = app.Title,
                    Detail = app.Message,
                    Type = $"https://microservices.micro.local/errors/{app.ErrorCode?.ToLowerInvariant() ?? "application"}",
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                };

                if (!string.IsNullOrWhiteSpace(app.ErrorCode))
                {
                    problem.Extensions["errorCode"] = app.ErrorCode;
                }

                foreach (var (key, value) in app.Extensions)
                {
                    problem.Extensions[key] = value;
                }

                break;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // Le client a coupe : rien a renvoyer.
                logger.LogDebug("Requete annulee par le client : {Path}", httpContext.Request.Path);
                return true;

            case TaskCanceledException or TimeoutException:
                logger.LogError(
                    exception,
                    "Timeout sur {Method} {Path}",
                    httpContext.Request.Method,
                    httpContext.Request.Path);

                problem = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.ServiceUnavailable,
                    Title = "Service indisponible",
                    Detail = "Le traitement a depasse le delai autorise. Merci de reessayer.",
                    Type = "https://microservices.micro.local/errors/timeout",
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                };
                problem.Extensions["errorCode"] = "TIMEOUT";
                break;

            default:
                logger.LogError(
                    exception,
                    "Erreur non geree sur {Method} {Path}",
                    httpContext.Request.Method,
                    httpContext.Request.Path);

                problem = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.InternalServerError,
                    Title = "Erreur interne",
                    Detail = "Une erreur inattendue est survenue. L'incident a ete journalise.",
                    Type = "https://microservices.micro.local/errors/internal",
                    Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}"
                };
                problem.Extensions["errorCode"] = "INTERNAL_ERROR";
                break;
        }

        problem.Extensions["correlationId"] = correlationId;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
