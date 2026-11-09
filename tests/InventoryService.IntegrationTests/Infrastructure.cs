using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GestionCommandes.IntegrationTests;

/// <summary>
/// Demarre le service dans un hote de test : base SQLite temporaire, cles de test,
/// seeding de demonstration applique comme en production.
/// </summary>
public sealed class ServiceFactory(IDictionary<string, string?>? surcharges = null) : WebApplicationFactory<Program>
{
    public const string CleJwt = "cle-jwt-de-test-microservices-gestion-commandes-2025";
    public const string CleInterne = "cle-interne-de-test-2025";

    private readonly string _dossierDonnees = Path.Combine(Path.GetTempPath(), "gc-it-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var valeurs = new Dictionary<string, string?>
            {
                ["Storage:DataPath"] = _dossierDonnees,
                ["Jwt:Key"] = CleJwt,
                ["Internal:ApiKey"] = CleInterne
            };

            if (surcharges is not null)
            {
                foreach (var (cle, valeur) in surcharges)
                {
                    valeurs[cle] = valeur;
                }
            }

            configuration.AddInMemoryCollection(valeurs);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            Directory.Delete(_dossierDonnees, recursive: true);
        }
        catch (IOException)
        {
            // Les fichiers SQLite peuvent etre encore ouverts par le processus de test.
        }
    }
}

/// <summary>Fabrique des jetons JWT signes avec la cle de test, pour simuler chaque role.</summary>
public static class Jetons
{
    public static string Creer(string role, Guid utilisateur, Guid? client = null, Guid? livreur = null)
    {
        var revendications = new List<Claim>
        {
            new("sub", utilisateur.ToString()),
            new("unique_name", "test@micro.local"),
            new("role", role)
        };

        if (client is { } idClient)
        {
            revendications.Add(new Claim("customer_id", idClient.ToString()));
        }

        if (livreur is { } idLivreur)
        {
            revendications.Add(new Claim("courier_id", idLivreur.ToString()));
        }

        var cle = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ServiceFactory.CleJwt));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "gestion-commandes-identity",
            Audience = "gestion-commandes-api",
            Subject = new ClaimsIdentity(revendications),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(cle, SecurityAlgorithms.HmacSha256)
        });
    }
}
