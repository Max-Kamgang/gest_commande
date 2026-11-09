using System.ComponentModel.DataAnnotations;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;

namespace GestionCommandes.IdentityService.Dtos;

/// <summary>Demande de connexion.</summary>
public sealed class LoginRequest
{
    /// <summary>Identifiant de connexion (email).</summary>
    [Required(ErrorMessage = "userName est obligatoire.")]
    [MaxLength(160)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Mot de passe en clair.</summary>
    [Required(ErrorMessage = "password est obligatoire.")]
    [MaxLength(200)]
    public string Password { get; set; } = string.Empty;
}

/// <summary>Jeton d'acces et profil associe.</summary>
public sealed class LoginResponse
{
    /// <summary>Jeton JWT a placer dans l'en-tete <c>Authorization: Bearer ...</c>.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Type de jeton (toujours <c>Bearer</c>).</summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Duree de validite restante, en secondes.</summary>
    public int ExpiresIn { get; set; }

    /// <summary>Date d'expiration absolue (UTC).</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Profil du compte connecte.</summary>
    public UserResponse User { get; set; } = new();
}

/// <summary>Profil utilisateur expose par l'API.</summary>
public sealed class UserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public Guid? CourierId { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
}

/// <summary>Creation d'un compte.</summary>
public sealed class CreateUserRequest
{
    [Required, EmailAddress(ErrorMessage = "userName doit etre une adresse email valide."), MaxLength(160)]
    public string UserName { get; set; } = string.Empty;

    [EmailAddress, MaxLength(160)]
    public string? Email { get; set; }

    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "Le mot de passe doit faire au moins 8 caracteres."), MaxLength(200)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Roles a attribuer. Au moins un role valide est requis.</summary>
    [Required, MinLength(1, ErrorMessage = "Au moins un role est requis.")]
    public List<string> Roles { get; set; } = [];

    /// <summary>Identifiant livreur (obligatoire pour le role Livreur).</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Identifiant client (obligatoire pour le role Client).</summary>
    public Guid? CustomerId { get; set; }
}

/// <summary>Mise a jour d'un compte.</summary>
public sealed class UpdateUserRequest
{
    [Required, MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(1, ErrorMessage = "Au moins un role est requis.")]
    public List<string> Roles { get; set; } = [];

    public Guid? CourierId { get; set; }

    public Guid? CustomerId { get; set; }
}

/// <summary>Activation / desactivation d'un compte.</summary>
public sealed class SetUserStatusRequest
{
    /// <summary>Nouvel etat du compte.</summary>
    [Required]
    public bool IsActive { get; set; }
}

/// <summary>Changement de mot de passe par l'utilisateur lui-meme.</summary>
public sealed class ChangePasswordRequest
{
    [Required, MaxLength(200)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8, ErrorMessage = "Le nouveau mot de passe doit faire au moins 8 caracteres."), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Reinitialisation de mot de passe par un administrateur.</summary>
public sealed class ResetPasswordRequest
{
    [Required, MinLength(8), MaxLength(200)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Filtres de recherche de comptes.</summary>
public sealed class UserQuery : PageQuery
{
    /// <summary>Recherche libre sur le nom, l'email ou l'identifiant de connexion.</summary>
    [MaxLength(120)]
    public string? Search { get; set; }

    /// <summary>Filtre par role exact.</summary>
    [MaxLength(40)]
    public string? Role { get; set; }

    /// <summary>Filtre par etat d'activation.</summary>
    public bool? IsActive { get; set; }
}

/// <summary>Liste des roles reconnus (pour les interfaces d'administration).</summary>
public static class RoleCatalog
{
    /// <summary>Roles disponibles.</summary>
    public static IReadOnlyList<string> All => Roles.All;
}
