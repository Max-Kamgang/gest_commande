using System.Threading.Channels;
using GestionCommandes.BuildingBlocks.Correlation;
using Microsoft.Extensions.Logging;

namespace GestionCommandes.BuildingBlocks.Events;

/// <summary>File interne bornee : protege le service si notification-service est lent ou absent.</summary>
public sealed class EventDispatchQueue
{
    private readonly Channel<IntegrationEvent> _channel =
        Channel.CreateBounded<IntegrationEvent>(new BoundedChannelOptions(1_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    /// <summary>Depot non bloquant.</summary>
    public bool TryWrite(IntegrationEvent integrationEvent) => _channel.Writer.TryWrite(integrationEvent);

    /// <summary>Lecture asynchrone par le service de fond.</summary>
    public IAsyncEnumerable<IntegrationEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Implementation de <see cref="IEventPublisher"/> adossee a <see cref="EventDispatchQueue"/>.</summary>
internal sealed class QueuedEventPublisher(
    EventDispatchQueue queue,
    ICorrelationIdAccessor correlationIdAccessor,
    ServiceIdentity identity,
    ILogger<QueuedEventPublisher> logger)
    : IEventPublisher
{
    public bool Publish(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        integrationEvent.Source = string.IsNullOrWhiteSpace(integrationEvent.Source)
            ? identity.Name
            : integrationEvent.Source;

        // Le Correlation-ID est capture ICI : le service de fond n'a plus de HttpContext.
        integrationEvent.CorrelationId ??= correlationIdAccessor.CorrelationId;

        var accepted = queue.TryWrite(integrationEvent);

        if (accepted)
        {
            logger.LogInformation(
                "Evenement {EventType} mis en file pour {Subject} (id {EventId})",
                integrationEvent.EventType,
                integrationEvent.Subject,
                integrationEvent.EventId);
        }
        else
        {
            logger.LogWarning(
                "File d'evenements saturee : {EventType} pour {Subject} n'a pas pu etre mis en file",
                integrationEvent.EventType,
                integrationEvent.Subject);
        }

        return accepted;
    }
}

/// <summary>Identite du service courant, injectee une fois au demarrage.</summary>
/// <param name="Name">Nom technique (ex. <c>order-service</c>).</param>
public sealed record ServiceIdentity(string Name);
