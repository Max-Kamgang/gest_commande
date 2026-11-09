using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.Shared.Security;

/// <summary>
/// Authentification de la plateforme, identique dans les quatre services :
/// un jeton JWT pour les utilisateurs, une cle partagee pour les appels entre
/// conteneurs. Les deux schemas cohabitent et les politiques acceptent l'un ou l'autre.
/// </summary>
public static class PlatformAuthentication
{
    /// <summary>Enregistre les deux schemas, les politiques et <see cref="ICurrentUser"/>.</summary>
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<InternalKeyOptions>()
            .Bind(configuration.GetSection(InternalKeyOptions.SectionName));

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var internalApiKey = configuration.GetSection(InternalKeyOptions.SectionName)["ApiKey"] ?? string.Empty;

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Conserve les noms de claims d'origine (sub, role) au lieu de les remapper.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    // Tolerance d'horloge : les VM et les conteneurs derivent de quelques secondes.
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "unique_name",
                    RoleClaimType = "role"
                };
            })
            .AddScheme<InternalKeySchemeOptions, InternalKeyHandler>(
                InternalKey.Scheme,
                options => options.ApiKey = internalApiKey);

        string[] schemes = [JwtBearerDefaults.AuthenticationScheme, InternalKey.Scheme];

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(schemes)
                .RequireAuthenticatedUser()
                .Build())
            .AddPolicy(Policies.BackOffice, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin, Roles.Gestionnaire, Roles.Service))
            .AddPolicy(Policies.Livraisons, policy => policy
                .AddAuthenticationSchemes(schemes)
                .RequireRole(Roles.Admin, Roles.Gestionnaire, Roles.Livreur, Roles.Service));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, JsonAuthorizationResultHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }
}

/// <summary>
/// Ecrit les reponses 401 et 403 en JSON, une seule fois.
///
/// Necessaire parce que deux schemas sont declares : sans cela, chacun tente
/// d'ecrire le refus et le second echoue sur une reponse deja commencee.
/// </summary>
internal sealed class JsonAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await WriteAsync(context, StatusCodes.Status401Unauthorized, "Non authentifie",
                "Un jeton valide est requis. Appelez POST /api/auth/login pour en obtenir un.");

            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteAsync(context, StatusCodes.Status403Forbidden, "Acces refuse",
                "Votre role ne permet pas d'acceder a cette ressource.");

            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteAsync(HttpContext context, int status, string title, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = $"{context.Request.Method} {context.Request.Path}"
        });
    }
}
