using System.Net.Http.Json;
using GestionCommandes.BuildingBlocks.Http;

namespace GestionCommandes.DeliveryService.Clients;

/// <summary>Notification de livraison effective envoyee a order-service.</summary>
public sealed class DeliveryCompletedDto
{
    /// <summary>Identifiant de la livraison.</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>Date effective de livraison.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Nom du destinataire.</summary>
    public string? ReceivedBy { get; set; }
}

/// <summary>Appels sortants vers order-service.</summary>
public interface IOrderClient
{
    /// <summary>
    /// Informe order-service qu'une livraison est terminee, afin qu'il passe la
    /// commande a l'etat Livree. L'endpoint cible est idempotent.
    /// </summary>
    Task NotifyDeliveredAsync(Guid orderId, DeliveryCompletedDto payload, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class OrderClient(HttpClient http, ILogger<OrderClient> logger) : IOrderClient
{
    /// <summary>Nom logique du service, utilise dans les messages d'erreur 503.</summary>
    public const string ServiceName = "order-service";

    public async Task NotifyDeliveredAsync(
        Guid orderId,
        DeliveryCompletedDto payload,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Appel de {Service} : notification de livraison de la commande {OrderId}",
            ServiceName,
            orderId);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/orders/{orderId}/delivered")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await http.SendSafeAsync(message, ServiceName, cancellationToken);

        await response.EnsureDownstreamSuccessAsync(ServiceName, cancellationToken);
    }
}
