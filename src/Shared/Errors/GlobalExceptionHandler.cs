using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GestionCommandes.Shared.Errors;

/// <summary>
/// Traduit toute exception en reponse JSON <c>ProblemDetails</c>.
/// Une <see cref="AppException"/> devient son code HTTP (404, 409...) et n'est
/// journalisee qu'en avertissement ; le reste devient un 500 journalise en erreur.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Le client a coupe la connexion : il n'y a plus personne a qui repondre.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        ProblemDetails problem;

        if (exception is AppException app)
        {
            logger.LogWarning(
                "Erreur metier {Status} sur {Method} {Path} : {Detail}",
                (int)app.StatusCode, httpContext.Request.Method, httpContext.Request.Path, app.Message);

            problem = new ProblemDetails
            {
                Status = (int)app.StatusCode,
                Title = app.Title,
                Detail = app.Message
            };
        }
        else
        {
            logger.LogError(
                exception,
                "Erreur non geree sur {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Erreur interne",
                Detail = "Une erreur inattendue est survenue. L'incident a ete journalise."
            };
        }

        problem.Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}";

        httpContext.Response.StatusCode = problem.Status!.Value;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
