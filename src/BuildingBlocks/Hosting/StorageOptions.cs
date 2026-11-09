using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GestionCommandes.BuildingBlocks.Hosting;

/// <summary>
/// Emplacement des donnees persistantes du service (base SQLite + logs fichier).
///
/// En conteneur Windows nanoserver le processus tourne sous <c>ContainerUser</c>,
/// qui n'a PAS le droit d'ecrire a la racine de <c>C:\</c>. On place donc les
/// donnees dans <c>C:\app\data</c>, dossier cree et autorise explicitement dans le
/// Dockerfile (<c>icacls ... /grant "ContainerUser:(OI)(CI)M"</c>), puis monte sur
/// <c>C:\microservices\data\&lt;service&gt;</c> cote hote via docker-compose.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Storage";

    /// <summary>Racine des donnees. Variable d'environnement : <c>STORAGE__DATAPATH</c>.</summary>
    public string DataPath { get; set; } = string.Empty;

    /// <summary>Sous-dossier des logs. Par defaut <c>&lt;DataPath&gt;\logs</c>.</summary>
    public string? LogsPath { get; set; }
}

/// <summary>Resolution et preparation des dossiers de donnees.</summary>
public static class StorageLocator
{
    /// <summary>
    /// Calcule le dossier de donnees du service, le cree si besoin et verifie
    /// qu'il est reellement accessible en ecriture.
    /// </summary>
    /// <param name="configuration">Configuration de l'application.</param>
    /// <param name="environment">Environnement d'hebergement (fournit le ContentRootPath).</param>
    /// <param name="serviceName">Nom technique du service (ex. <c>catalog-service</c>).</param>
    public static StoragePaths Resolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        var options = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();

        var dataPath = string.IsNullOrWhiteSpace(options.DataPath)
            // Hors conteneur (dotnet run) : un dossier « data » a cote du projet.
            ? Path.Combine(environment.ContentRootPath, "data")
            : options.DataPath;

        dataPath = Path.GetFullPath(dataPath);

        var logsPath = string.IsNullOrWhiteSpace(options.LogsPath)
            ? Path.Combine(dataPath, "logs")
            : Path.GetFullPath(options.LogsPath);

        EnsureWritable(dataPath, serviceName);
        EnsureWritable(logsPath, serviceName);

        return new StoragePaths(dataPath, logsPath, Path.Combine(dataPath, $"{serviceName}.db"));
    }

    private static void EnsureWritable(string path, string serviceName)
    {
        try
        {
            Directory.CreateDirectory(path);

            // Test d'ecriture reel : sous ContainerUser un dossier peut exister sans etre inscriptible.
            var probe = Path.Combine(path, $".write-probe-{Environment.ProcessId}");
            File.WriteAllText(probe, serviceName);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Le dossier de donnees '{path}' n'est pas accessible en ecriture pour le service '{serviceName}'. " +
                "En conteneur Windows, verifiez que le Dockerfile accorde les droits a ContainerUser " +
                "(icacls C:\\app\\data /grant \"ContainerUser:(OI)(CI)M\") et que le dossier hote monte " +
                "(C:\\microservices\\data\\<service>) autorise l'ecriture (script infra/04-users-ntfs.ps1).",
                ex);
        }
    }
}

/// <summary>Chemins resolus pour un service.</summary>
/// <param name="DataPath">Racine des donnees.</param>
/// <param name="LogsPath">Dossier des logs fichier.</param>
/// <param name="DatabaseFile">Fichier SQLite du service.</param>
public sealed record StoragePaths(string DataPath, string LogsPath, string DatabaseFile)
{
    /// <summary>Chaine de connexion SQLite correspondante.</summary>
    public string SqliteConnectionString => $"Data Source={DatabaseFile};Cache=Shared;Foreign Keys=True";
}
