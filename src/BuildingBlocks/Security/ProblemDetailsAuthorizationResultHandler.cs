using System.Text.Json;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>
/// Produit les reponses 401 et 403 au format <c>application/problem+json</c>.
///
/// Pourquoi ici et pas dans les evenements de JwtBearer : la plateforme declare
/// <b>deux</b> schemas d'authentification (JWT utilisateur et cle interne). Quand
/// une politique les liste tous les deux, l'intergiciel d'autorisation demande le
/// refus a <b>chaque</b> schema, et le second ecrivait dans une reponse deja
/// commencee. En interceptant le resultat d'autorisation une seule fois, on ecrit
/// un seul corps de reponse, sans exception parasite.
/// </summary>
internal sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Non authentifie",
                "Un jeton JWT valide est requis. Appelez POST /api/auth/login sur identity-service.",
                "UNAUTHENTICATED");

            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "Acces refuse",
                "Votre role ne permet pas d'acceder a cette ressource.",
                "FORBIDDEN");

            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail,
        string errorCode)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.Items.TryGetValue(CorrelationConstants.HeaderName, out var raw)
            ? raw as string
            : context.TraceIdentifier;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = $"https://microservices.micro.local/errors/{errorCode.ToLowerInvariant()}",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["instance"] = $"{context.Request.Method} {context.Request.Path}",
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
