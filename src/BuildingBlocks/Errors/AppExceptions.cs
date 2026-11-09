using System.Net;

namespace GestionCommandes.BuildingBlocks.Errors;

/// <summary>
/// Exception metier portant un code HTTP et un titre, convertie automatiquement
/// en reponse <c>application/problem+json</c> (RFC 9457) par le gestionnaire global.
/// </summary>
public class AppException(
    HttpStatusCode statusCode,
    string title,
    string detail,
    string? errorCode = null,
    IDictionary<string, object?>? extensions = null)
    : Exception(detail)
{
    /// <summary>Code HTTP a renvoyer au client.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Titre court et stable de l'erreur.</summary>
    public string Title { get; } = title;

    /// <summary>Code applicatif exploitable par le client (ex. <c>ORDER_INVALID_TRANSITION</c>).</summary>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>Donnees supplementaires ajoutees au ProblemDetails.</summary>
    public IDictionary<string, object?> Extensions { get; } =
        extensions ?? new Dictionary<string, object?>();
}

/// <summary>404 - la ressource demandee n'existe pas.</summary>
public sealed class NotFoundException(string resource, object key)
    : AppException(
        HttpStatusCode.NotFound,
        "Ressource introuvable",
        $"{resource} '{key}' est introuvable.",
        "NOT_FOUND");

/// <summary>400 - la requete est syntaxiquement valide mais metier-invalide.</summary>
public sealed class BadRequestException(string detail, string? errorCode = null)
    : AppException(HttpStatusCode.BadRequest, "Requete invalide", detail, errorCode ?? "BAD_REQUEST");

/// <summary>409 - conflit d'etat (transition interdite, concurrence, doublon).</summary>
public sealed class ConflictException(string detail, string? errorCode = null, IDictionary<string, object?>? extensions = null)
    : AppException(HttpStatusCode.Conflict, "Conflit d'etat", detail, errorCode ?? "CONFLICT", extensions);

/// <summary>403 - l'utilisateur est authentifie mais n'a pas le droit d'agir sur la ressource.</summary>
public sealed class ForbiddenException(string detail)
    : AppException(HttpStatusCode.Forbidden, "Acces refuse", detail, "FORBIDDEN");

/// <summary>
/// 503 - un microservice dont on depend est injoignable, en timeout, ou son
/// circuit-breaker est ouvert. On ne laisse JAMAIS remonter un 500 brut dans ce cas.
/// </summary>
public sealed class ServiceUnavailableException(string serviceName, string detail, Exception? inner = null)
    : AppException(
        HttpStatusCode.ServiceUnavailable,
        "Service indisponible",
        detail,
        "SERVICE_UNAVAILABLE",
        new Dictionary<string, object?> { ["dependency"] = serviceName })
{
    /// <summary>Nom logique du service en cause (ex. <c>inventory-service</c>).</summary>
    public string ServiceName { get; } = serviceName;

    /// <summary>Exception technique d'origine, journalisee mais non exposee au client.</summary>
    public Exception? InnerCause { get; } = inner;
}
