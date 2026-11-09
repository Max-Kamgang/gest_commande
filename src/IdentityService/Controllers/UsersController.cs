using GestionCommandes.IdentityService.Data;
using GestionCommandes.IdentityService.Domain;
using GestionCommandes.IdentityService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Controllers;

/// <summary>
/// Gestion des comptes. Reservee au role Admin : creer un compte, c'est accorder
/// des droits sur toute la plateforme.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public sealed class UsersController(IdentityDbContext context, ILogger<UsersController> logger) : ControllerBase
{
    /// <summary>Liste paginee des comptes.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserResponse>>> List(
        [FromQuery] UserQuery query,
        CancellationToken cancellationToken)
    {
        var users = context.Users.Include(u => u.Roles).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            users = users.Where(u =>
                EF.Functions.Like(u.UserName, $"%{search}%")
                || EF.Functions.Like(u.FullName, $"%{search}%")
                || EF.Functions.Like(u.Email, $"%{search}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            users = users.Where(u => u.Roles.Any(r => r.Role == query.Role));
        }

        if (query.IsActive is { } isActive)
        {
            users = users.Where(u => u.IsActive == isActive);
        }

        var page = await users
            .OrderBy(u => u.UserName)
            .ToPageAsync(query, UserMapper.ToResponse, cancellationToken);

        return Ok(page);
    }

    /// <summary>Renvoie un compte.</summary>
    /// <param name="id">Identifiant du compte.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        return Ok(UserMapper.ToResponse(user));
    }

    /// <summary>Cree un compte.</summary>
    /// <param name="request">Donnees du compte.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    public async Task<ActionResult<UserResponse>> Create(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var userName = request.UserName.Trim().ToLowerInvariant();

        if (await context.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            throw new ConflictException($"Le compte '{userName}' existe deja.");
        }

        var roles = ValidateRoles(request.Roles);

        // Un compte Livreur ou Client doit pointer vers l'entite metier correspondante,
        // sinon les regles « mes livraisons » et « mes commandes » ne peuvent pas s'appliquer.
        if (roles.Contains(Roles.Livreur) && request.CourierId is null)
        {
            throw new BadRequestException("courierId est obligatoire pour le role Livreur.");
        }

        if (roles.Contains(Roles.Client) && request.CustomerId is null)
        {
            throw new BadRequestException("customerId est obligatoire pour le role Client.");
        }

        var user = new AppUser
        {
            UserName = userName,
            Email = userName,
            FullName = request.FullName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            CourierId = request.CourierId,
            CustomerId = request.CustomerId,
            Roles = roles.Select(role => new UserRole { Role = role }).ToList()
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Compte {UserName} cree avec les roles {Roles}", user.UserName, string.Join(", ", roles));

        return CreatedAtAction(nameof(Get), new { id = user.Id }, UserMapper.ToResponse(user));
    }

    /// <summary>Met a jour un compte et ses roles.</summary>
    /// <param name="id">Identifiant du compte.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(
        Guid id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        var roles = ValidateRoles(request.Roles);

        user.FullName = request.FullName.Trim();
        user.Email = request.Email.Trim().ToLowerInvariant();
        user.CourierId = request.CourierId;
        user.CustomerId = request.CustomerId;

        // Mise a jour par difference : on retire les roles en trop puis on ajoute les
        // manquants. Vider la collection pour la reconstruire ferait tenter a EF Core
        // une suppression et une insertion de la meme cle dans le meme enregistrement.
        var existing = user.Roles.Select(r => r.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var obsolete in user.Roles
                     .Where(r => !roles.Contains(r.Role, StringComparer.OrdinalIgnoreCase))
                     .ToList())
        {
            user.Roles.Remove(obsolete);
        }

        foreach (var role in roles.Where(role => !existing.Contains(role)))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, Role = role });
        }

        await context.SaveChangesAsync(cancellationToken);

        return Ok(UserMapper.ToResponse(user));
    }

    /// <summary>Active ou desactive un compte.</summary>
    /// <param name="id">Identifiant du compte.</param>
    /// <param name="request">Nouvel etat.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/active")]
    public async Task<ActionResult<UserResponse>> SetActive(
        Guid id,
        SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        // Garde-fou : ne pas desactiver le dernier administrateur actif, sinon
        // plus personne ne peut administrer la plateforme.
        if (!request.IsActive && user.Roles.Any(r => r.Role == Roles.Admin))
        {
            var otherActiveAdmins = await context.Users
                .CountAsync(u => u.Id != id && u.IsActive && u.Roles.Any(r => r.Role == Roles.Admin), cancellationToken);

            if (otherActiveAdmins == 0)
            {
                throw new ConflictException("Impossible de desactiver le dernier administrateur actif.");
            }
        }

        user.IsActive = request.IsActive;
        await context.SaveChangesAsync(cancellationToken);

        return Ok(UserMapper.ToResponse(user));
    }

    /// <summary>Reinitialise le mot de passe d'un compte.</summary>
    /// <param name="id">Identifiant du compte.</param>
    /// <param name="request">Nouveau mot de passe.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        Guid id,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new NotFoundException("Le compte", id);

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Mot de passe reinitialise pour {UserName}", user.UserName);

        return NoContent();
    }

    /// <summary>Verifie que les roles demandes existent et sont attribuables.</summary>
    private static List<string> ValidateRoles(List<string> requested)
    {
        var roles = requested
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unknown = roles
            .Where(role => !Roles.Attribuables.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new BadRequestException(
                $"Role(s) inconnu(s) : {string.Join(", ", unknown)}. " +
                $"Roles valides : {string.Join(", ", Roles.Attribuables)}.");
        }

        // Le role technique Service n'est jamais attribuable a un compte humain :
        // il est deja exclu par Roles.Attribuables, ce controle le rend explicite.
        return roles
            .Select(role => Roles.Attribuables.First(valid => string.Equals(valid, role, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
