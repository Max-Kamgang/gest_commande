using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>Expose l'identite issue du JWT de la requete courante.</summary>
public interface ICurrentUser
{
    /// <summary>Vrai si la requete porte un jeton valide.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Identifiant du compte (claim <c>sub</c>).</summary>
    Guid? UserId { get; }

    /// <summary>Nom d'utilisateur / email de connexion.</summary>
    string? UserName { get; }

    /// <summary>Identifiant du livreur lie au compte, s'il existe.</summary>
    Guid? CourierId { get; }

    /// <summary>Identifiant du client lie au compte, s'il existe.</summary>
    Guid? CustomerId { get; }

    /// <summary>Roles portes par le jeton.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Teste l'appartenance a un role.</summary>
    bool IsInRole(string role);

    /// <summary>Vrai pour Admin ou Gestionnaire.</summary>
    bool IsBackOffice { get; }
}

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid(JwtRegisteredClaimNames.Sub) ?? ReadGuid(ClaimTypes.NameIdentifier);

    public string? UserName =>
        Principal?.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value
        ?? Principal?.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? Principal?.FindFirst(ClaimTypes.Name)?.Value;

    public Guid? CourierId => ReadGuid(AppClaims.CourierId);

    public Guid? CustomerId => ReadGuid(AppClaims.CustomerId);

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll("role").Select(c => c.Value)
            .Concat(Principal.FindAll(ClaimTypes.Role).Select(c => c.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
        ?? [];

    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    // Le nom de membre « Roles » masque la classe statique du meme nom : on qualifie explicitement.
    public bool IsBackOffice =>
        IsInRole(global::GestionCommandes.BuildingBlocks.Security.Roles.Admin)
        || IsInRole(global::GestionCommandes.BuildingBlocks.Security.Roles.Gestionnaire);

    private Guid? ReadGuid(string claimType)
    {
        var value = Principal?.FindFirst(claimType)?.Value;
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
