using System.Net.Http.Json;
using GestionCommandes.BuildingBlocks.Http;

namespace GestionCommandes.OrderService.Clients;

/// <summary>Appels sortants vers catalog-service.</summary>
public interface ICatalogClient
{
    /// <summary>Recupere les prix courants des produits d'une commande.</summary>
    Task<PricingResponseDto> GetPricingAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class CatalogClient(HttpClient http, ILogger<CatalogClient> logger) : ICatalogClient
{
    /// <summary>Nom logique du service, utilise dans les messages d'erreur 503.</summary>
    public const string ServiceName = "catalog-service";

    public async Task<PricingResponseDto> GetPricingAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken)
    {
        var payload = new PricingRequestDto { ProductIds = productIds.Distinct().ToList() };

        logger.LogInformation(
            "Appel de {Service} : tarification de {Count} produit(s)",
            ServiceName,
            payload.ProductIds.Count);

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/products/pricing")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await http.SendSafeAsync(request, ServiceName, cancellationToken);

        return await response.ReadContentOrThrowAsync<PricingResponseDto>(ServiceName, cancellationToken);
    }
}
