using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Data;
using GestionCommandes.IdentityService.Domain;
using GestionCommandes.IdentityService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.IdentityService.Services;

/// <summary>Logique metier des comptes utilisateurs.</summary>
public sealed class UserService(
    IdentityDbContext context,
    ITokenService tokenService,
    ILogger<UserService> logger)
{
    /// <summary>
    /// Authentifie un compte et emet un jeton.
    /// Le message d'erreur reste volontairement identique que l'identifiant soit
    /// inconnu ou le mot de passe faux (pas d'enumeration de comptes).
    /// </summary>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = Normalize(request.UserName);

        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.UserName == normalized, cancellationToken);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                await context.SaveChangesAsync(cancellationToken);
            }

            logger.LogWarning("Echec de connexion pour l'identifiant {UserName}", normalized);

            throw new AppException(
                System.Net.HttpStatusCode.Unauthorized,
                "Identifiants invalides",
                "Identifiant ou mot de passe incorrect.",
                "INVALID_CREDENTIALS");
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Connexion refusee : compte desactive {UserName}", normalized);

            throw new ForbiddenException(
                "Ce compte est desactive. Contactez un administrateur.");
        }

        var (token, expiresAt) = tokenService.CreateAccessToken(user);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.FailedLoginCount = 0;
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Connexion reussie de {UserName} (roles : {Roles})",
            user.UserName,
            string.Join(", ", user.Roles.Select(r => r.Role)));

        return new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expiresAt,
            ExpiresIn = (int)(expiresAt - DateTimeOffset.UtcNow).TotalSeconds,
            User = ToResponse(user)
        };
    }

    /// <summary>Liste paginee et filtree des comptes.</summary>
    public async Task<PagedResult<UserResponse>> SearchAsync(UserQuery query, CancellationToken cancellationToken)
    {
        var source = context.Users.Include(u => u.Roles).AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(u =>
                EF.Functions.Like(u.UserName, $"%{term}%") ||
                EF.Functions.Like(u.Email, $"%{term}%") ||
                EF.Functions.Like(u.FullName, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            source = source.Where(u => u.Roles.Any(r => r.Role == role));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(u => u.IsActive == isActive);
        }

        return await source
            .OrderBy(u => u.FullName)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere un compte par identifiant.</summary>
    public async Task<UserResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        return ToResponse(user);
    }

    /// <summary>Cree un compte.</summary>
    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var roles = ValidateRoles(request.Roles);
        var userName = Normalize(request.UserName);

        if (await context.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
        {
            throw new ConflictException(
                $"Un compte existe deja avec l'identifiant '{userName}'.",
                "USER_ALREADY_EXISTS");
        }

        EnsureBusinessLinks(roles, request.CourierId, request.CustomerId);

        var user = new AppUser
        {
            UserName = userName,
            Email = Normalize(string.IsNullOrWhiteSpace(request.Email) ? request.UserName : request.Email!),
            FullName = request.FullName.Trim(),
            PasswordHash = PasswordHasher.Hash(request.Password),
            CourierId = request.CourierId,
            CustomerId = request.CustomerId,
            IsActive = true,
            Roles = roles.Select(r => new UserRole { Role = r }).ToList()
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Compte {UserName} cree avec les roles {Roles}", user.UserName, string.Join(", ", roles));

        return ToResponse(user);
    }

    /// <summary>Met a jour le profil et les roles d'un compte.</summary>
    public async Task<UserResponse> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        var roles = ValidateRoles(request.Roles);
        EnsureBusinessLinks(roles, request.CourierId, request.CustomerId);

        user.FullName = request.FullName.Trim();
        user.Email = Normalize(request.Email);
        user.CourierId = request.CourierId;
        user.CustomerId = request.CustomerId;

        // Remplacement complet de la liste des roles.
        user.Roles.RemoveAll(existing => !roles.Contains(existing.Role));
        foreach (var role in roles.Where(role => user.Roles.All(r => r.Role != role)))
        {
            user.Roles.Add(new UserRole { UserId = user.Id, Role = role });
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Compte {UserName} mis a jour", user.UserName);

        return ToResponse(user);
    }

    /// <summary>Active ou desactive un compte.</summary>
    public async Task<UserResponse> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        // Garde-fou : on ne desactive pas le dernier administrateur actif.
        if (!isActive && user.Roles.Any(r => r.Role == Roles.Admin))
        {
            var remainingAdmins = await context.Users
                .CountAsync(u => u.IsActive && u.Id != id && u.Roles.Any(r => r.Role == Roles.Admin), cancellationToken);

            if (remainingAdmins == 0)
            {
                throw new ConflictException(
                    "Impossible de desactiver le dernier compte administrateur actif.",
                    "LAST_ADMIN");
            }
        }

        user.IsActive = isActive;
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Compte {UserName} {Etat}",
            user.UserName,
            isActive ? "active" : "desactive");

        return ToResponse(user);
    }

    /// <summary>Change le mot de passe de l'utilisateur connecte.</summary>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                   ?? throw new NotFoundException("Le compte", userId);

        if (!PasswordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new BadRequestException("Le mot de passe actuel est incorrect.", "INVALID_CURRENT_PASSWORD");
        }

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Mot de passe modifie pour {UserName}", user.UserName);
    }

    /// <summary>Reinitialise le mot de passe d'un compte (administrateur).</summary>
    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
                   ?? throw new NotFoundException("Le compte", id);

        user.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        user.FailedLoginCount = 0;
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Mot de passe reinitialise pour {UserName}", user.UserName);
    }

    /// <summary>Supprime un compte.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le compte", id);

        if (user.Roles.Any(r => r.Role == Roles.Admin))
        {
            var remainingAdmins = await context.Users
                .CountAsync(u => u.Id != id && u.Roles.Any(r => r.Role == Roles.Admin), cancellationToken);

            if (remainingAdmins == 0)
            {
                throw new ConflictException(
                    "Impossible de supprimer le dernier compte administrateur.",
                    "LAST_ADMIN");
            }
        }

        context.Users.Remove(user);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Compte {UserName} supprime", user.UserName);
    }

    private static List<string> ValidateRoles(IEnumerable<string> requested)
    {
        var roles = requested
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roles.Count == 0)
        {
            throw new BadRequestException("Au moins un role doit etre fourni.", "ROLE_REQUIRED");
        }

        var unknown = roles
            .Where(r => !Roles.All.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (unknown.Count > 0)
        {
            throw new BadRequestException(
                $"Role(s) inconnu(s) : {string.Join(", ", unknown)}. Valeurs acceptees : {string.Join(", ", Roles.All)}.",
                "UNKNOWN_ROLE");
        }

        // Normalisation sur la casse officielle.
        return roles
            .Select(r => Roles.All.First(known => known.Equals(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static void EnsureBusinessLinks(IReadOnlyCollection<string> roles, Guid? courierId, Guid? customerId)
    {
        if (roles.Contains(Roles.Livreur) && courierId is null)
        {
            throw new BadRequestException(
                "courierId est obligatoire pour un compte de role Livreur (identifiant du livreur dans delivery-service).",
                "COURIER_ID_REQUIRED");
        }

        if (roles.Contains(Roles.Client) && customerId is null)
        {
            throw new BadRequestException(
                "customerId est obligatoire pour un compte de role Client (identifiant du client dans order-service).",
                "CUSTOMER_ID_REQUIRED");
        }
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    internal static UserResponse ToResponse(AppUser user) => new()
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
