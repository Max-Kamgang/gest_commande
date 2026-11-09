using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace GestionCommandes.BuildingBlocks.Correlation;

/// <summary>
/// Middleware d'entree : recupere l'en-tete <c>X-Correlation-ID</c> (ou en genere un),
/// le place dans <see cref="HttpContext.Items"/>, l'ajoute au contexte de log Serilog
/// et le renvoie dans la reponse. C'est la base de la tracabilite de bout en bout.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[CorrelationConstants.HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;

        // L'en-tete est ecrit avant que la reponse ne soit envoyee.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(CorrelationConstants.LogPropertyName, correlationId))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationConstants.HeaderName, out var header))
        {
            var candidate = header.ToString();
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 128)
            {
                return candidate.Trim();
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
