using System.Text;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Hosting;
using GestionCommandes.NotificationService.Domain;

namespace GestionCommandes.NotificationService.Services;

/// <summary>Resultat d'une diffusion simulee.</summary>
/// <param name="Success">Vrai si la diffusion a abouti.</param>
/// <param name="Log">Compte rendu lisible, stocke avec la notification.</param>
public sealed record SendResult(bool Success, string Log);

/// <summary>Diffusion d'une notification vers son canal.</summary>
public interface INotificationSender
{
    /// <summary>Diffuse la notification et renvoie le compte rendu.</summary>
    Task<SendResult> SendAsync(Notification notification, CancellationToken cancellationToken);
}

/// <summary>
/// Envoi <b>simule</b> d'email et de SMS.
///
/// Aucun serveur SMTP ni passerelle SMS n'est requis : le message est ecrit dans
/// un dossier « outbox » du volume de donnees, sous la forme d'un fichier texte
/// horodate, et journalise par Serilog. C'est suffisant pour la demonstration et
/// cela laisse une trace verifiable a l'examinateur.
///
/// Pour un envoi reel, il suffirait de remplacer cette implementation
/// (l'interface <see cref="INotificationSender"/> ne changerait pas).
/// </summary>
public sealed class SimulatedNotificationSender(
    StoragePaths paths,
    ILogger<SimulatedNotificationSender> logger)
    : INotificationSender
{
    /// <summary>Nom du sous-dossier contenant les messages simules.</summary>
    public const string OutboxFolderName = "outbox";

    public async Task<SendResult> SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Le canal « Interne » ne sort pas de l'application : rien a ecrire.
        if (string.Equals(notification.Channel, EventChannels.Interne, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Notification interne {Title} enregistree (aucun envoi externe) [{EventType}]",
                notification.Title,
                notification.EventType);

            return new SendResult(true, "Notification interne : consultable dans l'application, aucun envoi externe.");
        }

        if (string.IsNullOrWhiteSpace(notification.Recipient))
        {
            logger.LogWarning(
                "Notification {Title} de canal {Channel} sans destinataire : diffusion impossible",
                notification.Title,
                notification.Channel);

            return new SendResult(
                false,
                $"Aucun destinataire renseigne pour le canal {notification.Channel} : diffusion abandonnee.");
        }

        try
        {
            var day = notification.ReceivedAt.ToUniversalTime().ToString("yyyy-MM-dd");
            var folder = Path.Combine(paths.DataPath, OutboxFolderName, day);
            Directory.CreateDirectory(folder);

            var fileName =
                $"{notification.ReceivedAt.ToUniversalTime():HHmmss}-{notification.Channel.ToLowerInvariant()}-" +
                $"{Sanitize(notification.Subject)}-{notification.Id.ToString("N")[..8]}.txt";

            var path = Path.Combine(folder, fileName);

            await File.WriteAllTextAsync(path, Render(notification), Encoding.UTF8, cancellationToken);

            logger.LogInformation(
                "{Channel} simule envoye a {Recipient} : « {Title} » -> {File}",
                notification.Channel,
                notification.Recipient,
                notification.Title,
                path);

            return new SendResult(
                true,
                $"{notification.Channel} simule ecrit dans {Path.Combine(OutboxFolderName, day, fileName)}");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Echec de la diffusion simulee de la notification {NotificationId}",
                notification.Id);

            return new SendResult(false, $"Echec de la diffusion simulee : {ex.Message}");
        }
    }

    /// <summary>Met en forme le message tel qu'il serait envoye.</summary>
    private static string Render(Notification notification)
    {
        var builder = new StringBuilder();

        builder.AppendLine("=========================================================");
        builder.AppendLine($" {notification.Channel.ToUpperInvariant()} SIMULE - plateforme de gestion des commandes");
        builder.AppendLine("=========================================================");
        builder.AppendLine($"Date           : {notification.ReceivedAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"Destinataire   : {notification.Recipient}");
        builder.AppendLine($"Objet          : {notification.Title}");
        builder.AppendLine($"Gravite        : {notification.Severity}");
        builder.AppendLine($"Type           : {notification.EventType}");
        builder.AppendLine($"Emetteur       : {notification.Source}");
        builder.AppendLine($"Sujet          : {notification.Subject}");
        builder.AppendLine($"Correlation-ID : {notification.CorrelationId ?? "-"}");
        builder.AppendLine("---------------------------------------------------------");
        builder.AppendLine(notification.Message);

        if (!string.IsNullOrWhiteSpace(notification.PayloadJson))
        {
            builder.AppendLine("---------------------------------------------------------");
            builder.AppendLine("Donnees techniques :");
            builder.AppendLine(notification.PayloadJson);
        }

        builder.AppendLine("=========================================================");

        return builder.ToString();
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "sans-sujet";
        }

        var cleaned = new string(value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return cleaned.Length > 40 ? cleaned[..40] : cleaned;
    }
}
