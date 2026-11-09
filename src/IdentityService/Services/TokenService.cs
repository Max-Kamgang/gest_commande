using System.Security.Claims;
using System.Text;
using GestionCommandes.IdentityService.Domain;
using GestionCommandes.Shared.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.IdentityService.Services;

/// <summary>
/// Emission des jetons JWT, signes en HMAC-SHA256 avec la cle partagee.
///
/// identity-service est le SEUL service qui signe des jetons ; les autres se
/// contentent de les valider avec la meme cle (variable <c>JWT__KEY</c>).
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Produit un jeton d'acces pour le compte fourni.</summary>
    /// <param name="user">Compte authentifie, roles charges.</param>
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(AppUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("unique_name", user.UserName),
            new("email", user.Email),
            new(AppClaims.FullName, user.FullName)
        };

        // Un claim « role » par role : c'est ce que lisent les politiques d'autorisation.
        claims.AddRange(user.Roles.Select(r => new Claim("role", r.Role)));

        if (user.CourierId is { } courierId)
        {
            claims.Add(new Claim(AppClaims.CourierId, courierId.ToString()));
        }

        if (user.CustomerId is { } customerId)
        {
            claims.Add(new Claim(AppClaims.CustomerId, customerId.ToString()));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);

        return (token, expiresAt);
    }
}
