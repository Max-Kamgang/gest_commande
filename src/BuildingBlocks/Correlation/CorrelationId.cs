using Microsoft.AspNetCore.Http;

namespace GestionCommandes.BuildingBlocks.Correlation;

/// <summary>Constantes liees a la correlation des requetes entre microservices.</summary>
public static class CorrelationConstants
{
    /// <summary>Nom de l'en-tete HTTP portant l'identifiant de correlation.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Nom de la propriete de log Serilog.</summary>
    public const string LogPropertyName = "CorrelationId";
}

/// <summary>Donne acces a l'identifiant de correlation de la requete en cours.</summary>
public interface ICorrelationIdAccessor
{
    /// <summary>Identifiant de correlation courant (jamais nul : genere si absent).</summary>
    string CorrelationId { get; }
}

/// <summary>
/// Implementation basee sur <see cref="IHttpContextAccessor"/>.
/// Hors contexte HTTP (taches de fond, seeding) un identifiant local est genere.
/// </summary>
internal sealed class HttpCorrelationIdAccessor(IHttpContextAccessor accessor) : ICorrelationIdAccessor
{
    private readonly string _fallback = Guid.NewGuid().ToString("N");

    public string CorrelationId
    {
        get
        {
            var context = accessor.HttpContext;
            if (context is null)
            {
                return _fallback;
            }

            if (context.Items.TryGetValue(CorrelationConstants.HeaderName, out var value) && value is string s)
            {
                return s;
            }

            return _fallback;
        }
    }
}
