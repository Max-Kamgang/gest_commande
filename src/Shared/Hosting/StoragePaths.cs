using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace GestionCommandes.Shared.Hosting;

/// <summary>
/// Emplacement de la base SQLite du service.
///
/// En conteneur Windows le processus tourne sous <c>ContainerUser</c>, qui n'a pas
/// le droit d'ecrire a la racine de <c>C:\</c>. Les donnees vont donc dans
/// <c>C:\app\data</c>, dossier autorise explicitement dans le Dockerfile et monte
/// cote hote sur <c>C:\microservices\data\&lt;service&gt;</c>.
/// Hors conteneur (<c>dotnet run</c>), un dossier <c>data</c> a cote du projet.
/// </summary>
/// <param name="DataPath">Dossier des donnees.</param>
/// <param name="DatabaseFile">Fichier SQLite du service.</param>
public sealed record StoragePaths(string DataPath, string DatabaseFile)
{
    /// <summary>Chaine de connexion SQLite.</summary>
    public string ConnectionString => $"Data Source={DatabaseFile};Cache=Shared;Foreign Keys=True";

    /// <summary>Resout le dossier de donnees et verifie qu'il est accessible en ecriture.</summary>
    public static StoragePaths Resolve(
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        var configured = configuration["Storage:DataPath"];

        var dataPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "data")
            : configured);

        try
        {
            Directory.CreateDirectory(dataPath);

            // Test d'ecriture reel : sous ContainerUser un dossier peut exister sans etre inscriptible.
            var probe = Path.Combine(dataPath, $".probe-{Environment.ProcessId}");
            File.WriteAllText(probe, serviceName);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                $"Le dossier '{dataPath}' n'est pas accessible en ecriture pour {serviceName}. " +
                "En conteneur, verifiez la ligne icacls du Dockerfile et les droits du dossier hote monte.",
                ex);
        }

        return new StoragePaths(dataPath, Path.Combine(dataPath, $"{serviceName}.db"));
    }
}
