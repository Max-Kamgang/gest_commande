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
    string? downstreamErrorCode)
    : AppException(
        statusCode,
        title,
        detail,
        downstreamErrorCode ?? "DOWNSTREAM_REJECTED",
        new Dictionary<string, object?>
        {
            ["dependency"] = serviceName,
            ["dependencyStatus"] = (int)statusCode
        })
{
    /// <summary>Service qui a refuse l'appel.</summary>
    public string ServiceName { get; } = serviceName;
}
