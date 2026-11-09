namespace GestionCommandes.BuildingBlocks.Events;

/// <summary>Types d'evenements d'integration echanges entre services.</summary>
public static class EventTypes
{
    public const string OrderConfirmed = "order.confirmed";
    public const string OrderShipped = "order.shipped";
    public const string OrderCancelled = "order.cancelled";
    public const string OrderDelivered = "order.delivered";
    public const string StockLow = "inventory.stock-low";
    public const string StockMovement = "inventory.movement";
    public const string DeliveryAssigned = "delivery.assigned";
    public const string DeliveryCompleted = "delivery.completed";
}

/// <summary>Niveaux de gravite d'une notification.</summary>
public static class EventSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Critical = "Critical";
}

/// <summary>Canaux de diffusion simules par notification-service.</summary>
public static class EventChannels
{
    public const string Email = "Email";
    public const string Sms = "Sms";
    public const string Interne = "Interne";
}

/// <summary>
/// Enveloppe d'evenement d'integration publiee en HTTP POST vers notification-service.
/// Contrat partage : le producteur et le consommateur referencent la meme classe.
/// </summary>
public sealed class IntegrationEvent
{
    /// <summary>Identifiant unique de l'evenement (sert a l'idempotence cote consommateur).</summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    /// <summary>Type d'evenement (voir <see cref="EventTypes"/>).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Service emetteur (ex. <c>order-service</c>).</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Horodatage UTC de l'evenement.</summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Sujet metier concerne (numero de commande, reference produit...).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Titre lisible de la notification.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Corps du message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Gravite (voir <see cref="EventSeverities"/>).</summary>
    public string Severity { get; set; } = EventSeverities.Info;

    /// <summary>Canal demande (voir <see cref="EventChannels"/>).</summary>
    public string Channel { get; set; } = EventChannels.Interne;

    /// <summary>Destinataire (email ou numero), facultatif.</summary>
    public string? Recipient { get; set; }

    /// <summary>Donnees techniques additionnelles.</summary>
    public Dictionary<string, string> Data { get; set; } = [];

    /// <summary>Correlation-ID de la requete d'origine.</summary>
    public string? CorrelationId { get; set; }
}
