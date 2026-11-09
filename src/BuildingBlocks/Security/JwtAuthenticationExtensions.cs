using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>Enregistrement de l'authentification JWT Bearer, identique dans tous les services.</summary>
public static class JwtAuthenticationExtensions
{
    /// <summary>
    /// Ajoute la validation des jetons JWT (signature, emetteur, audience, duree de vie)
    /// et renvoie des reponses 401/403 au format ProblemDetails.
    /// </summary>
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                      ?? throw new InvalidOperationException(
                          "La section de configuration 'Jwt' est absente. Verifiez appsettings.json ou les variables d'environnement JWT__*.");

        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key doit etre renseignee et faire au moins 32 caracteres (variable d'environnement JWT__KEY).");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
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

                // 401 / 403 au format problem+json plutot qu'un corps vide.
                bearer.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteProblemAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Non authentifie",
                            "Un jeton JWT valide est requis. Appelez POST /api/auth/login.",
                            "UNAUTHENTICATED");
                    },
                    OnForbidden = async context => await WriteProblemAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "Acces refuse",
                        "Votre role ne permet pas d'acceder a cette ressource.",
                        "FORBIDDEN")
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.BackOffice, policy =>
                policy.RequireRole(Roles.Admin, Roles.Gestionnaire))
            .AddPolicy(AuthPolicies.AdminOnly, policy =>
                policy.RequireRole(Roles.Admin))
            .AddPolicy(AuthPolicies.Livraisons, policy =>
                policy.RequireRole(Roles.Admin, Roles.Gestionnaire, Roles.Livreur));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail,
        string errorCode)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var correlationId = context.Items.TryGetValue(Correlation.CorrelationConstants.HeaderName, out var raw)
            ? raw as string
            : context.TraceIdentifier;

        var payload = new Dictionary<string, object?>
        {
            ["type"] = $"https://microservices.micro.local/errors/{errorCode.ToLowerInvariant()}",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["instance"] = $"{context.Request.Method} {context.Request.Path}",
            ["errorCode"] = errorCode,
            ["correlationId"] = correlationId
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
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
}
