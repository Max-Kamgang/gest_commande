using System.Net.Http.Json;
using GestionCommandes.BuildingBlocks.Http;

namespace GestionCommandes.OrderService.Clients;

/// <summary>Appels sortants vers delivery-service.</summary>
public interface IDeliveryClient
{
    /// <summary>Cree la livraison d'une commande expediee. Idempotent sur le numero de commande.</summary>
    Task<DeliveryResultDto> CreateDeliveryAsync(
        CreateDeliveryRequestDto request,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class DeliveryClient(HttpClient http, ILogger<DeliveryClient> logger) : IDeliveryClient
{
    /// <summary>Nom logique du service, utilise dans les messages d'erreur 503.</summary>
    public const string ServiceName = "delivery-service";

    public async Task<DeliveryResultDto> CreateDeliveryAsync(
        CreateDeliveryRequestDto request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Appel de {Service} : creation de la livraison de la commande {OrderNumber}",
            ServiceName,
            request.OrderNumber);

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/deliveries")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await http.SendSafeAsync(message, ServiceName, cancellationToken);

        return await response.ReadContentOrThrowAsync<DeliveryResultDto>(ServiceName, cancellationToken);
    }
}
