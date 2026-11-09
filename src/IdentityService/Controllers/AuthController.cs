using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Dtos;
using GestionCommandes.IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.IdentityService.Controllers;

/// <summary>Connexion et gestion du compte courant.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController(UserService users, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Authentifie un utilisateur et renvoie un jeton JWT.</summary>
    /// <remarks>Point d'entree unique de l'authentification pour toute la plateforme.</remarks>
    /// <response code="200">Jeton emis.</response>
    /// <response code="401">Identifiant ou mot de passe incorrect.</response>
    /// <response code="403">Compte desactive.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
        => Ok(await users.LoginAsync(request, cancellationToken));

    /// <summary>Renvoie le profil du compte authentifie.</summary>
    /// <response code="200">Profil du compte courant.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
                     ?? throw new AppException(
                         System.Net.HttpStatusCode.Unauthorized,
                         "Jeton invalide",
                         "Le jeton ne contient pas d'identifiant d'utilisateur exploitable.",
                         "INVALID_TOKEN");

        return Ok(await users.GetAsync(userId, cancellationToken));
    }

    /// <summary>Change le mot de passe du compte authentifie.</summary>
    /// <response code="204">Mot de passe modifie.</response>
    /// <response code="400">Mot de passe actuel incorrect.</response>
    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
                     ?? throw new AppException(
                         System.Net.HttpStatusCode.Unauthorized,
                         "Jeton invalide",
                         "Le jeton ne contient pas d'identifiant d'utilisateur exploitable.",
                         "INVALID_TOKEN");

        await users.ChangePasswordAsync(userId, request, cancellationToken);

        return NoContent();
    }

    /// <summary>Liste les roles reconnus par la plateforme.</summary>
    /// <response code="200">Roles disponibles.</response>
    [HttpGet("roles")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetRoles() => Ok(RoleCatalog.All);
}
