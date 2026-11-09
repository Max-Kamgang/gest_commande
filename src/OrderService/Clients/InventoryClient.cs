using System.Net.Http.Json;
using GestionCommandes.BuildingBlocks.Http;

namespace GestionCommandes.OrderService.Clients;

/// <summary>Appels sortants vers inventory-service (participant de la saga).</summary>
public interface IInventoryClient
{
    /// <summary>Reserve le stock d'une commande.</summary>
    Task<ReservationResultDto> ReserveAsync(ReservationRequestDto request, CancellationToken cancellationToken);

    /// <summary>Libere la reservation d'une commande (compensation).</summary>
    Task<ReservationResultDto> ReleaseAsync(string reference, string reason, CancellationToken cancellationToken);

    /// <summary>Transforme la reservation en sortie definitive (expedition).</summary>
    Task<ReservationResultDto> CommitAsync(string reference, string reason, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class InventoryClient(HttpClient http, ILogger<InventoryClient> logger) : IInventoryClient
{
    /// <summary>Nom logique du service, utilise dans les messages d'erreur 503.</summary>
    public const string ServiceName = "inventory-service";

    public async Task<ReservationResultDto> ReserveAsync(
        ReservationRequestDto request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Appel de {Service} : reservation {Reference} ({Lines} ligne(s))",
            ServiceName,
            request.Reference,
            request.Lines.Count);

        using var message = new HttpRequestMessage(HttpMethod.Post, "api/reservations")
        {
            Content = JsonContent.Create(request)
        };

        using var response = await http.SendSafeAsync(message, ServiceName, cancellationToken);

        return await response.ReadContentOrThrowAsync<ReservationResultDto>(ServiceName, cancellationToken);
    }

    public async Task<ReservationResultDto> ReleaseAsync(
        string reference,
        string reason,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Appel de {Service} : liberation de la reservation {Reference} ({Reason})",
            ServiceName,
            reference,
            reason);

        var uri = $"api/reservations/{Uri.EscapeDataString(reference)}?reason={Uri.EscapeDataString(reason)}";

        using var message = new HttpRequestMessage(HttpMethod.Delete, uri);
        using var response = await http.SendSafeAsync(message, ServiceName, cancellationToken);

        return await response.ReadContentOrThrowAsync<ReservationResultDto>(ServiceName, cancellationToken);
    }

    public async Task<ReservationResultDto> CommitAsync(
        string reference,
        string reason,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Appel de {Service} : consommation de la reservation {Reference}",
            ServiceName,
            reference);

        var uri = $"api/reservations/{Uri.EscapeDataString(reference)}/commit?reason={Uri.EscapeDataString(reason)}";

        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        using var response = await http.SendSafeAsync(message, ServiceName, cancellationToken);

        return await response.ReadContentOrThrowAsync<ReservationResultDto>(ServiceName, cancellationToken);
    }
}
