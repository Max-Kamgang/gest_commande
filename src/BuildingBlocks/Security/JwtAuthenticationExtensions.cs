using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>
/// Enregistrement de l'authentification de la plateforme, identique dans tous les services :
/// <list type="number">
///   <item>JWT Bearer pour les appels d'utilisateurs,</item>
///   <item>cle interne partagee pour les appels de service a service.</item>
/// </list>
/// </summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Ajoute la validation des jetons JWT (signature, emetteur, audience, duree de vie),
    /// le schema de cle interne, et renvoie des reponses 401/403 au format ProblemDetails.
    /// </summary>
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validation au demarrage (ValidateOnStart) ; les valeurs sont lues a l'execution,
        // pas a l'enregistrement : la configuration est alors complete (variables
        // d'environnement, fichiers, surcharges de test).
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<InternalKeyOptions>()
            .Bind(configuration.GetSection(InternalKeyOptions.SectionName));

        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<InternalKeyOptions, InternalKeyAuthenticationHandler>(
                InternalKeyDefaults.Scheme,
                schemeOptions => schemeOptions.ApiKey =
                    configuration.GetSection(InternalKeyOptions.SectionName)["ApiKey"] ?? string.Empty);

        // Les deux schemas sont acceptes partout : HttpContext.User agrege les identites.
        string[] schemes = [JwtBearerDefaults.AuthenticationScheme, InternalKeyDefaults.Scheme];

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(schemes)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(AuthPolicies.BackOffice, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin, Roles.Gestionnaire))
            .AddPolicy(AuthPolicies.AdminOnly, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin))
            // Le role technique Service est inclus : order-service cree les bordereaux
            // de livraison. Les attributs [Authorize] se cumulent (ET logique), donc une
            // politique posee sur un controleur ne doit pas exclure les appels internes
            // que ses actions autorisent.
            .AddPolicy(AuthPolicies.Livraisons, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin, Roles.Gestionnaire, Roles.Livreur, Roles.Service))
            .AddPolicy(AuthPolicies.BackOfficeOuService, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin, Roles.Gestionnaire, Roles.Service))
            .AddPolicy(AuthPolicies.Interne, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Service));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

}

/// <summary>
/// Configure la validation du JWT a partir de <see cref="JwtOptions"/>, au moment de la
/// premiere utilisation (et non a l'enregistrement).
/// </summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwt) : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions bearer)
    {
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var options = jwt.Value;

        bearer.MapInboundClaims = false; // conserve les noms de claims d'origine (sub, role...)
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.Issuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = "role"
        };
    }
}

/// <summary>Noms des politiques d'autorisation partagees.</summary>
public static class AuthPolicies
{
    /// <summary>Admin ou Gestionnaire.</summary>
    public const string BackOffice = "back-office";

    /// <summary>Admin uniquement.</summary>
    public const string AdminOnly = "admin-only";

    /// <summary>Admin, Gestionnaire ou Livreur.</summary>
    public const string Livraisons = "livraisons";

    /// <summary>Admin, Gestionnaire ou appel interne de service a service.</summary>
    public const string BackOfficeOuService = "back-office-ou-service";

    /// <summary>Appel interne de service a service uniquement.</summary>
    public const string Interne = "interne";
}
