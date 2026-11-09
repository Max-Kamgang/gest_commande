using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.Shared.Security;

/// <summary>Parametres des jetons JWT (section de configuration <c>Jwt</c>).</summary>
public sealed class JwtOptions
{
    /// <summary>Nom de la section.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Cle de signature HMAC-SHA256. Variable d'environnement <c>JWT__KEY</c>.</summary>
    [Required, MinLength(32, ErrorMessage = "La cle JWT doit faire au moins 32 caracteres.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Emetteur attendu.</summary>
    public string Issuer { get; set; } = "gestion-commandes";

    /// <summary>Audience attendue.</summary>
    public string Audience { get; set; } = "gestion-commandes-api";

    /// <summary>Duree de vie du jeton, en minutes.</summary>
    [Range(5, 1440)]
    public int AccessTokenMinutes { get; set; } = 120;
}

/// <summary>Cle partagee pour les appels de service a service (section <c>Internal</c>).</summary>
public sealed class InternalKeyOptions
{
    /// <summary>Nom de la section.</summary>
    public const string SectionName = "Internal";

    /// <summary>
    /// Cle connue des seuls conteneurs. La gateway la SUPPRIME des requetes
    /// entrantes : un client exterieur ne peut donc pas s'en servir.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>Adresses des autres services (section <c>Services</c>).</summary>
public sealed class ServiceEndpointsOptions
{
    /// <summary>Nom de la section.</summary>
    public const string SectionName = "Services";

    /// <summary>Adresse de identity-service.</summary>
    public string Identity { get; set; } = "http://localhost:5001";

    /// <summary>Adresse de catalog-service.</summary>
    public string Catalog { get; set; } = "http://localhost:5002";

    /// <summary>Adresse de order-service.</summary>
    public string Order { get; set; } = "http://localhost:5003";

    /// <summary>Adresse de delivery-service.</summary>
    public string Delivery { get; set; } = "http://localhost:5004";
}
