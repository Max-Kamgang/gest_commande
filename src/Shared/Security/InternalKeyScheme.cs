using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionCommandes.Shared.Security;

/// <summary>Constantes du schema d'authentification de service a service.</summary>
public static class InternalKey
{
    /// <summary>Nom du schema.</summary>
    public const string Scheme = "InternalKey";

    /// <summary>En-tete HTTP portant la cle.</summary>
    public const string HeaderName = "X-Internal-Key";
}

/// <summary>Options du schema <c>InternalKey</c>.</summary>
public sealed class InternalKeySchemeOptions : AuthenticationSchemeOptions
{
    /// <summary>Cle attendue, lue dans la section <c>Internal</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Authentifie les appels entre conteneurs grace a une cle partagee.
/// Un appel interne recoit le role technique <see cref="Roles.Service"/> et
/// jamais un role metier : il ne peut donc pas se faire passer pour un utilisateur.
/// </summary>
public sealed class InternalKeyHandler(
    IOptionsMonitor<InternalKeySchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<InternalKeySchemeOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Pas d'en-tete : ce n'est pas un appel interne, le schema JWT prend la main.
        if (!Request.Headers.TryGetValue(InternalKey.HeaderName, out var provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = Options.ApiKey;

        if (string.IsNullOrWhiteSpace(expected) || !ConstantTimeEquals(provided.ToString(), expected))
        {
            Logger.LogWarning(
                "Cle interne invalide sur {Method} {Path}", Request.Method, Request.Path);

            return Task.FromResult(AuthenticateResult.Fail("Cle interne invalide."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, "service-interne"),
                new Claim("role", Roles.Service)
            ],
            InternalKey.Scheme,
            ClaimTypes.Name,
            "role");

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), InternalKey.Scheme)));
    }

    /// <summary>Comparaison en temps constant : ne fuite pas la cle par mesure de temps.</summary>
    private static bool ConstantTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);

        return bytesA.Length == bytesB.Length && CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}

/// <summary>Ajoute la cle interne sur tous les appels sortants vers un autre service.</summary>
public sealed class InternalKeyHeaderHandler(IOptions<InternalKeyOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var key = options.Value.ApiKey;

        if (!string.IsNullOrWhiteSpace(key) && !request.Headers.Contains(InternalKey.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(InternalKey.HeaderName, key);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
