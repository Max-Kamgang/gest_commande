using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace GestionCommandes.Shared.Security;

/// <summary>Identite portee par le jeton de la requete en cours.</summary>
public interface ICurrentUser
{
    /// <summary>Identifiant du compte (claim <c>sub</c>).</summary>
    Guid? UserId { get; }

    /// <summary>Identifiant de connexion, utilise pour tracer qui agit.</summary>
    string UserName { get; }

    /// <summary>Identifiant du livreur lie au compte, s'il existe.</summary>
    Guid? CourierId { get; }

    /// <summary>Identifiant du client lie au compte, s'il existe.</summary>
    Guid? CustomerId { get; }

    /// <summary>Teste l'appartenance a un role.</summary>
    bool IsInRole(string role);

    /// <summary>Vrai pour Admin, Gestionnaire ou un appel interne de service.</summary>
    bool IsBackOffice { get; }
}

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => ReadGuid("sub") ?? ReadGuid(ClaimTypes.NameIdentifier);

    public string UserName =>
        Principal?.FindFirst("unique_name")?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? "systeme";

    public Guid? CourierId => ReadGuid(AppClaims.CourierId);

    public Guid? CustomerId => ReadGuid(AppClaims.CustomerId);

    public bool IsInRole(string role) =>
        Principal?.FindAll("role").Any(c => string.Equals(c.Value, role, StringComparison.OrdinalIgnoreCase)) == true
        || Principal?.IsInRole(role) == true;

    public bool IsBackOffice =>
        IsInRole(Security.Roles.Admin)
        || IsInRole(Security.Roles.Gestionnaire)
        || IsInRole(Security.Roles.Service);

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirst(claimType)?.Value, out var parsed) ? parsed : null;
}
