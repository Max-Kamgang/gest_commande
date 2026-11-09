using GestionCommandes.IdentityService.Data;
using GestionCommandes.IdentityService.Domain;
using GestionCommandes.IdentityService.Dtos;
using GestionCommandes.IdentityService.Services;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Controllers;

/// <summary>Connexion et consultation du compte courant.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IdentityDbContext context,
    TokenService tokens,
    ICurrentUser currentUser,
    ILogger<AuthController> logger)
    : ControllerBase
{
    /// <summary>Authentifie un compte et renvoie un jeton JWT.</summary>
    /// <param name="request">Identifiant et mot de passe.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="200">Connexion reussie.</response>
    /// <response code="401">Identifiants invalides ou compte desactive.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.UserName == request.UserName, cancellationToken);

        // Meme message et meme duree de traitement que l'identifiant existe ou non :
        // on ne revele pas quels comptes existent.
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            logger.LogWarning("Echec de connexion pour {UserName}", request.UserName);

            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Connexion refusee",
                Detail = "Identifiant ou mot de passe incorrect."
            });
        }

        if (!user.IsActive)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Compte desactive",
                Detail = "Ce compte est desactive. Contactez un administrateur."
            });
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        var (token, expiresAt) = tokens.CreateAccessToken(user);

        logger.LogInformation("Connexion reussie de {UserName}", user.UserName);

        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            User = UserMapper.ToResponse(user)
        });
    }

    /// <summary>Renvoie le profil du compte porte par le jeton.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Le jeton ne porte pas d'identifiant de compte.");

        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Le compte", userId);

        return Ok(UserMapper.ToResponse(user));
    }

    /// <summary>Change le mot de passe du compte connecte.</summary>
    /// <param name="request">Ancien et nouveau mot de passe.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Le jeton ne porte pas d'identifiant de compte.");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("Le compte", userId);

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("Le mot de passe actuel est incorrect.");
        }

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Mot de passe modifie pour {UserName}", user.UserName);

        return NoContent();
    }

    /// <summary>Liste les roles attribuables, pour les ecrans d'administration.</summary>
    [HttpGet("roles")]
    [AllowAnonymous]
    public ActionResult<IEnumerable<string>> GetRoles() => Ok(Roles.Attribuables);
}

/// <summary>Projection d'un compte vers le format expose par l'API.</summary>
public static class UserMapper
{
    /// <summary>Convertit un compte en reponse API (sans jamais exposer l'empreinte).</summary>
    /// <param name="user">Compte, roles charges.</param>
    public static UserResponse ToResponse(AppUser user) => new()
    {
        Id = user.Id,
        UserName = user.UserName,
        Email = user.Email,
        FullName = user.FullName,
        IsActive = user.IsActive,
        CourierId = user.CourierId,
        CustomerId = user.CustomerId,
        CreatedAt = user.CreatedAt,
        LastLoginAt = user.LastLoginAt,
        Roles = user.Roles.Select(r => r.Role).OrderBy(r => r).ToList()
    };
}
