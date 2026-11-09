using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.IdentityService.Domain;

/// <summary>Compte utilisateur de la plateforme.</summary>
public class AppUser
{
    /// <summary>Identifiant technique.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identifiant de connexion (adresse email).</summary>
    [MaxLength(160)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Adresse email de contact.</summary>
    [MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Nom complet affichable.</summary>
    [MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Empreinte PBKDF2 du mot de passe : jamais le mot de passe lui-meme.</summary>
    [MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Un compte desactive ne peut plus se connecter.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Identifiant du livreur associe, pour un compte de role Livreur. C'est ce
    /// qui permet a delivery-service de verifier qu'un livreur ne touche qu'a
    /// SES livraisons.
    /// </summary>
    public Guid? CourierId { get; set; }

    /// <summary>Identifiant du client associe, pour un compte de role Client.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Date de creation (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date de derniere connexion reussie (UTC).</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Roles attribues au compte.</summary>
    public List<UserRole> Roles { get; set; } = [];
}

/// <summary>Association compte / role : un compte peut porter plusieurs roles.</summary>
public class UserRole
{
    /// <summary>Compte concerne.</summary>
    public Guid UserId { get; set; }

    /// <summary>Nom du role (voir <c>Roles</c> dans le projet Shared).</summary>
    [MaxLength(40)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Navigation vers le compte.</summary>
    public AppUser? User { get; set; }
}
