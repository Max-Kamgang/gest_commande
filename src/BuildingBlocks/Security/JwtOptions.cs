using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>Parametres de signature et de validation des jetons JWT (section <c>Jwt</c>).</summary>
public sealed class JwtOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Cle symetrique HMAC-SHA256. Fournie par la variable d'environnement <c>JWT__KEY</c>.</summary>
    [Required, MinLength(32, ErrorMessage = "La cle JWT doit faire au moins 32 caracteres.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Emetteur attendu.</summary>
    [Required]
    public string Issuer { get; set; } = "gestion-commandes-identity";

    /// <summary>Audience attendue.</summary>
    [Required]
    public string Audience { get; set; } = "gestion-commandes-api";

    /// <summary>Duree de vie du jeton d'acces, en minutes.</summary>
    [Range(5, 1440)]
    public int AccessTokenMinutes { get; set; } = 120;

    /// <summary>Tolerance d'horloge, en secondes (utile entre VM et conteneurs).</summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;
}
