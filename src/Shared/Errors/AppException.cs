using System.Net;

namespace GestionCommandes.Shared.Errors;

/// <summary>
/// Erreur metier portant son code HTTP. Le gestionnaire global la traduit en
/// reponse JSON lisible : on ne laisse jamais remonter un 500 pour un cas prevu.
/// </summary>
public class AppException(HttpStatusCode statusCode, string title, string detail)
    : Exception(detail)
{
    /// <summary>Code HTTP a renvoyer.</summary>
    public HttpStatusCode StatusCode { get; } = statusCode;

    /// <summary>Titre court de l'erreur.</summary>
    public string Title { get; } = title;
}

/// <summary>404 : la ressource demandee n'existe pas.</summary>
public sealed class NotFoundException(string resource, object key)
    : AppException(HttpStatusCode.NotFound, "Ressource introuvable", $"{resource} '{key}' est introuvable.");

/// <summary>400 : requete recevable mais invalide sur le plan metier.</summary>
public sealed class BadRequestException(string detail)
    : AppException(HttpStatusCode.BadRequest, "Requete invalide", detail);

/// <summary>409 : conflit d'etat (transition interdite, doublon, stock insuffisant).</summary>
public sealed class ConflictException(string detail)
    : AppException(HttpStatusCode.Conflict, "Conflit d'etat", detail);

/// <summary>403 : utilisateur authentifie mais sans droit sur cette ressource.</summary>
public sealed class ForbiddenException(string detail)
    : AppException(HttpStatusCode.Forbidden, "Acces refuse", detail);

/// <summary>503 : un service dont on depend ne repond pas.</summary>
public sealed class ServiceUnavailableException(string serviceName, string detail)
    : AppException(HttpStatusCode.ServiceUnavailable, $"Service {serviceName} indisponible", detail);
