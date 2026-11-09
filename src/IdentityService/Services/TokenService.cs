using System.Security.Claims;
using System.Text;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.IdentityService.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.IdentityService.Services;

/// <summary>Emission des jetons JWT signes en HMAC-SHA256.</summary>
public interface ITokenService
{
    /// <summary>Produit un jeton d'acces pour le compte fourni.</summary>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AppUser user);
}

/// <inheritdoc />
public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(AppClaims.FullName, user.FullName)
        };

        // Un claim « role » par role : c'est ce que lisent les [Authorize(Roles = ...)].
        claims.AddRange(user.Roles.Select(r => new Claim("role", r.Role)));

        if (user.CourierId is { } courierId)
        {
            claims.Add(new Claim(AppClaims.CourierId, courierId.ToString()));
        }

        if (user.CustomerId is { } customerId)
        {
            claims.Add(new Claim(AppClaims.CustomerId, customerId.ToString()));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }
            .CreateToken(descriptor);

        return (token, expiresAt);
    }
}
