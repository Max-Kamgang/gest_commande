using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>Constantes de l'authentification de service a service.</summary>
public static class InternalKeyDefaults
{
    /// <summary>Nom du schema d'authentification.</summary>
    public const string Scheme = "InternalKey";

    /// <summary>En-tete HTTP portant la cle interne.</summary>
    public const string HeaderName = "X-Internal-Key";
}

/// <summary>Options du schema <c>InternalKey</c> (section <c>Internal</c>).</summary>
public sealed class InternalKeyOptions : AuthenticationSchemeOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Internal";

    /// <summary>
    /// Cle partagee entre les microservices, fournie par la variable d'environnement
    /// <c>INTERNAL__APIKEY</c>. Elle n'est JAMAIS exposee aux clients : seule la
    /// gateway et les services la connaissent.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Authentifie les appels <b>machine a machine</b> (saga de commande, rappel de
/// livraison, diffusion d'evenements) grace a une cle partagee.
///
/// Les appels d'utilisateurs restent authentifies par JWT : les deux schemas
/// coexistent et les politiques d'autorisation acceptent explicitement l'un ou
/// l'autre. Un appel interne obtient le role technique <c>Service</c>, jamais un
/// role metier : il ne peut donc pas se substituer a un utilisateur.
/// </summary>
public sealed class InternalKeyAuthenticationHandler(
    IOptionsMonitor<InternalKeyOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<InternalKeyOptions>(options, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(InternalKeyDefaults.HeaderName, out var provided))
        {
            // Pas d'en-tete : ce n'est pas un appel interne, on laisse le schema JWT repondre.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = Options.ApiKey;

        if (string.IsNullOrWhiteSpace(expected))
        {
            Logger.LogWarning(
                "Un appel porte l'en-tete {Header} alors qu'aucune cle interne n'est configuree (INTERNAL__APIKEY).",
                InternalKeyDefaults.HeaderName);

            return Task.FromResult(AuthenticateResult.Fail("Cle interne non configuree."));
        }

        var candidate = provided.ToString();

        if (!CryptographicEquals(candidate, expected))
        {
            Logger.LogWarning(
                "Cle interne invalide pour {Method} {Path} depuis {RemoteIp}",
                Request.Method,
                Request.Path,
                Context.Connection.RemoteIpAddress);

            return Task.FromResult(AuthenticateResult.Fail("Cle interne invalide."));
        }

        var caller = Request.Headers.UserAgent.ToString();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(caller) ? "service-interne" : caller),
            new Claim("role", Roles.Service)
        ], InternalKeyDefaults.Scheme, ClaimTypes.Name, "role");

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            InternalKeyDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>Comparaison en temps constant, pour ne pas fuiter la cle par mesure de temps.</summary>
    private static bool CryptographicEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);

        return bytesA.Length == bytesB.Length
               && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}

/// <summary>
/// <see cref="DelegatingHandler"/> ajoutant la cle interne sur tous les appels
/// sortants entre microservices.
/// </summary>
public sealed class InternalKeyPropagationHandler(IOptions<InternalKeyOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var key = options.Value.ApiKey;

        if (!string.IsNullOrWhiteSpace(key) && !request.Headers.Contains(InternalKeyDefaults.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(InternalKeyDefaults.HeaderName, key);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
