using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>Parametres propres a la gateway (section <c>Gateway</c>).</summary>
public sealed class GatewayOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Gateway";

    /// <summary>
    /// Adresse du client web, cible de la route de repli « / ».
    /// Variable d'environnement : <c>GATEWAY__WEBCLIENTURL</c>.
    /// </summary>
    [Required]
    public string WebClientUrl { get; set; } = "http://localhost:5010";

    /// <summary>Origines autorisees par CORS.</summary>
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:5010",
        "http://localhost:5000",
        "http://192.168.10.10",
        "https://192.168.10.10"
    ];

    /// <summary>Requetes autorisees par minute et par client, toutes routes confondues.</summary>
    [Range(10, 100_000)]
    public int RequestsPerMinute { get; set; } = 300;

    /// <summary>Tentatives de connexion autorisees par minute et par client.</summary>
    [Range(1, 1_000)]
    public int LoginAttemptsPerMinute { get; set; } = 15;

    /// <summary>Nombre de requetes mises en file d'attente avant rejet.</summary>
    [Range(0, 1_000)]
    public int QueueLimit { get; set; } = 0;
}
