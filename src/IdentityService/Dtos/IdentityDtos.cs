using System.ComponentModel.DataAnnotations;
using GestionCommandes.Shared.Web;

namespace GestionCommandes.IdentityService.Dtos;

/// <summary>Demande de connexion.</summary>
public sealed class LoginRequest
{
    /// <summary>Identifiant de connexion (email).</summary>
    [Required(ErrorMessage = "userName est obligatoire."), MaxLength(160)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Mot de passe en clair.</summary>
    [Required(ErrorMessage = "password est obligatoire."), MaxLength(200)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Jeton d'acces et profil du compte connecte.</summary>
public sealed class LoginResponse
{
    /// <summary>Jeton a placer dans l'en-tete <c>Authorization: Bearer ...</c>.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Type de jeton, toujours <c>Bearer</c>.</summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Date d'expiration (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Profil du compte.</summary>
    public UserResponse User { get; set; } = new();
}

/// <summary>Profil utilisateur expose par l'API.</summary>
public sealed class UserResponse
{
    /// <summary>Identifiant du compte.</summary>
    public Guid Id { get; set; }

    /// <summary>Identifiant de connexion.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Nom complet.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Compte actif.</summary>
    public bool IsActive { get; set; }

    /// <summary>Livreur lie au compte.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Client lie au compte.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Derniere connexion reussie.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Roles du compte.</summary>
    public IReadOnlyList<string> Roles { get; set; } = [];
}

/// <summary>Creation d'un compte.</summary>
public sealed class CreateUserRequest
{
    /// <summary>Identifiant de connexion (email).</summary>
    [Required, EmailAddress(ErrorMessage = "userName doit etre une adresse email valide."), MaxLength(160)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Nom complet.</summary>
    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Mot de passe initial.</summary>
    [Required, MinLength(8, ErrorMessage = "Le mot de passe doit faire au moins 8 caracteres."), MaxLength(200)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Roles a attribuer : au moins un.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins un role est requis.")]
    public List<string> Roles { get; set; } = [];

    /// <summary>Identifiant livreur, obligatoire pour le role Livreur.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Identifiant client, obligatoire pour le role Client.</summary>
    public Guid? CustomerId { get; set; }
}

/// <summary>Mise a jour d'un compte.</summary>
public sealed class UpdateUserRequest
{
    /// <summary>Nom complet.</summary>
    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Roles du compte : au moins un.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins un role est requis.")]
    public List<string> Roles { get; set; } = [];

    /// <summary>Identifiant livreur lie.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Identifiant client lie.</summary>
    public Guid? CustomerId { get; set; }
}

/// <summary>Activation ou desactivation.</summary>
public sealed class SetActiveRequest
{
    /// <summary>Nouvel etat.</summary>
    [Required]
    public bool IsActive { get; set; }
}

/// <summary>Changement de mot de passe par l'utilisateur lui-meme.</summary>
public sealed class ChangePasswordRequest
{
    /// <summary>Mot de passe actuel.</summary>
    [Required, MaxLength(200)]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>Nouveau mot de passe.</summary>
    [Required, MinLength(8, ErrorMessage = "Le nouveau mot de passe doit faire au moins 8 caracteres."), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Reinitialisation de mot de passe par un administrateur.</summary>
public sealed class ResetPasswordRequest
{
    /// <summary>Nouveau mot de passe.</summary>
    [Required, MinLength(8, ErrorMessage = "Le mot de passe doit faire au moins 8 caracteres."), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Filtres de recherche de comptes.</summary>
public sealed class UserQuery : PageQuery
{
    /// <summary>Recherche libre sur le nom, l'email ou l'identifiant.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Filtre par role.</summary>
    [MaxLength(40)]
    public string? Role { get; set; }

    /// <summary>Filtre par etat d'activation.</summary>
    public bool? IsActive { get; set; }
}
