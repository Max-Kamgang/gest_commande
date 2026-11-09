using System.Net;
using GestionCommandes.BuildingBlocks.Errors;

namespace GestionCommandes.BuildingBlocks.Http;

/// <summary>
/// Un service appele a repondu une erreur <b>metier</b> (4xx) : la faute est dans la
/// demande, pas dans la disponibilite. Le code HTTP d'origine est conserve afin que
/// l'appelant le relaie tel quel (ex. 409 « stock insuffisant »).
/// </summary>
public sealed class DownstreamRejectedException(
    string serviceName,
    HttpStatusCode statusCode,
    string title,
    string detail,
    string? downstreamErrorCode,
    IDictionary<string, object?>? downstreamExtensions = null)
    : AppException(
        statusCode,
        title,
        detail,
        downstreamErrorCode ?? "DOWNSTREAM_REJECTED",
        BuildExtensions(serviceName, statusCode, downstreamExtensions))
{
    /// <summary>
    /// Recopie les extensions utiles du ProblemDetails d'origine (par exemple la
    /// liste <c>shortages</c> renvoyee par inventory-service) afin que le client
    /// final dispose du meme niveau de detail, sans perdre la trace du service
    /// responsable.
    /// </summary>
    private static Dictionary<string, object?> BuildExtensions(
        string serviceName,
        HttpStatusCode statusCode,
        IDictionary<string, object?>? downstreamExtensions)
    {
        // Champs techniques propres au service appele : ils ne sont pas relayes.
        string[] ignored = ["correlationId", "service", "traceId", "errorCode", "instance"];

        var extensions = new Dictionary<string, object?>
        {
            ["dependency"] = serviceName,
            ["dependencyStatus"] = (int)statusCode
        };

        if (downstreamExtensions is null)
        {
            return extensions;
        }

        foreach (var (key, value) in downstreamExtensions)
        {
            if (!ignored.Contains(key, StringComparer.OrdinalIgnoreCase) && !extensions.ContainsKey(key))
            {
                extensions[key] = value;
            }
        }

        return extensions;
    }

    /// <summary>Service qui a refuse l'appel.</summary>
    public string ServiceName { get; } = serviceName;
}
