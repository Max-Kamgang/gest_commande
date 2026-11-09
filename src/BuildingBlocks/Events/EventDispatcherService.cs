using System.Net.Http.Json;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GestionCommandes.BuildingBlocks.Events;

/// <summary>
/// Service de fond qui vide la file d'evenements vers notification-service.
/// Tout echec est journalise (avec le Correlation-ID d'origine) et l'evenement est
/// abandonne : la disponibilite du flux metier passe avant la notification.
/// </summary>
internal sealed class EventDispatcherService(
    EventDispatchQueue queue,
    IHttpClientFactory httpClientFactory,
    ILogger<EventDispatcherService> logger)
    : BackgroundService
{
    /// <summary>Nom du HttpClient nomme utilise pour joindre notification-service.</summary>
    public const string HttpClientName = "notification-events";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Dispatcher d'evenements demarre");

        try
        {
            await foreach (var integrationEvent in queue.ReadAllAsync(stoppingToken))
            {
                await SendAsync(integrationEvent, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Dispatcher d'evenements arrete");
        }
    }

    private async Task SendAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/events")
            {
                Content = JsonContent.Create(integrationEvent)
            };

            if (!string.IsNullOrWhiteSpace(integrationEvent.CorrelationId))
            {
                request.Headers.TryAddWithoutValidation(
                    CorrelationConstants.HeaderName,
                    integrationEvent.CorrelationId);
            }

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Evenement {EventType} ({EventId}) diffuse a notification-service [corr {CorrelationId}]",
                    integrationEvent.EventType,
                    integrationEvent.EventId,
                    integrationEvent.CorrelationId);
            }
            else
            {
                logger.LogWarning(
                    "notification-service a refuse l'evenement {EventType} ({EventId}) : HTTP {StatusCode} [corr {CorrelationId}]",
                    integrationEvent.EventType,
                    integrationEvent.EventId,
                    (int)response.StatusCode,
                    integrationEvent.CorrelationId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fire-and-forget : on journalise et on continue, jamais d'exception remontee.
            logger.LogWarning(
                ex,
                "Echec de diffusion de l'evenement {EventType} ({EventId}) vers notification-service [corr {CorrelationId}]",
                integrationEvent.EventType,
                integrationEvent.EventId,
                integrationEvent.CorrelationId);
        }
    }
}
