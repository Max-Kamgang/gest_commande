using GestionCommandes.OrderService.Domain;
using GestionCommandes.Shared.Web;

namespace GestionCommandes.OrderService.Clients;

// ===========================================================================
//  Contrats echanges avec les autres services. Ce sont des copies LOCALES :
//  order-service ne partage aucune classe metier avec catalog-service ni
//  delivery-service, il ne connait que la forme JSON des echanges.
// ===========================================================================

/// <summary>Une ligne a reserver chez catalog-service.</summary>
/// <param name="ProductId">Produit.</param>
/// <param name="Quantity">Quantite.</param>
public sealed record ReserveLine(Guid ProductId, int Quantity);

/// <summary>Demande de reservation envoyee a catalog-service.</summary>
/// <param name="Reference">Numero de commande.</param>
/// <param name="Lines">Lignes a reserver.</param>
public sealed record ReserveStockRequest(string Reference, List<ReserveLine> Lines);

/// <summary>Ligne reservee, avec le prix catalogue a figer.</summary>
/// <param name="ProductId">Produit.</param>
/// <param name="Sku">Reference article.</param>
/// <param name="ProductName">Libelle.</param>
/// <param name="Quantity">Quantite reservee.</param>
/// <param name="UnitPrice">Prix unitaire du moment.</param>
public sealed record ReservedLine(Guid ProductId, string Sku, string ProductName, int Quantity, decimal UnitPrice);

/// <summary>Reponse de catalog-service a une reservation.</summary>
/// <param name="Id">Identifiant de la reservation.</param>
/// <param name="Reference">Numero de commande.</param>
/// <param name="Status">Etat de la reservation.</param>
/// <param name="Currency">Devise des prix.</param>
/// <param name="Lines">Lignes reservees et tarifees.</param>
public sealed record ReservationResult(
    Guid Id,
    string Reference,
    string Status,
    string Currency,
    List<ReservedLine> Lines);

/// <summary>Ligne du bordereau envoyee a delivery-service.</summary>
/// <param name="ProductId">Produit.</param>
/// <param name="Sku">Reference article.</param>
/// <param name="ProductName">Libelle.</param>
/// <param name="Quantity">Quantite a livrer.</param>
public sealed record DeliveryLineRequest(Guid ProductId, string? Sku, string? ProductName, int Quantity);

/// <summary>Demande de creation de bordereau envoyee a delivery-service.</summary>
/// <param name="OrderId">Commande d'origine.</param>
/// <param name="OrderNumber">Numero de commande, cle de non-doublon.</param>
/// <param name="CustomerId">Client.</param>
/// <param name="CustomerName">Nom du client.</param>
/// <param name="CustomerPhone">Telephone du client.</param>
/// <param name="ShippingAddress">Adresse de livraison.</param>
/// <param name="ShippingCity">Ville de livraison.</param>
/// <param name="Lines">Contenu a livrer.</param>
public sealed record CreateDeliveryRequest(
    Guid OrderId,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string? CustomerPhone,
    string? ShippingAddress,
    string? ShippingCity,
    List<DeliveryLineRequest> Lines);

/// <summary>Reponse de delivery-service a la creation d'un bordereau.</summary>
/// <param name="Id">Identifiant du bordereau.</param>
/// <param name="Reference">Reference lisible du bordereau.</param>
public sealed record DeliveryCreatedResult(Guid Id, string Reference);

/// <summary>
/// Appels de order-service vers catalog-service (stock et prix).
///
/// Les trois operations sont rejouables cote catalogue : en cas de doute sur une
/// reponse perdue, on peut rappeler sans rien doubler.
/// </summary>
public sealed class CatalogClient(HttpClient client)
{
    private const string ServiceName = "catalog-service";

    /// <summary>Reserve le stock d'une commande et recupere les prix a figer.</summary>
    /// <param name="order">Commande, lignes chargees.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public Task<ReservationResult> ReserveAsync(Order order, CancellationToken cancellationToken)
    {
        var request = new ReserveStockRequest(
            order.Number,
            order.Lines.Select(line => new ReserveLine(line.ProductId, line.Quantity)).ToList());

        return client.SendAsync<ReservationResult>(
            ServiceName, HttpMethod.Post, "api/stock/reservations", request, cancellationToken);
    }

    /// <summary>Libere la reservation d'une commande : le stock redevient disponible.</summary>
    /// <param name="orderNumber">Numero de commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public Task ReleaseAsync(string orderNumber, CancellationToken cancellationToken) =>
        client.SendAsync(
            ServiceName, HttpMethod.Post, $"api/stock/reservations/{orderNumber}/release", null, cancellationToken);

    /// <summary>Transforme la reservation en sortie de stock definitive.</summary>
    /// <param name="orderNumber">Numero de commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public Task ShipAsync(string orderNumber, CancellationToken cancellationToken) =>
        client.SendAsync(
            ServiceName, HttpMethod.Post, $"api/stock/reservations/{orderNumber}/ship", null, cancellationToken);
}

/// <summary>Appels de order-service vers delivery-service.</summary>
public sealed class DeliveryClient(HttpClient client)
{
    private const string ServiceName = "delivery-service";

    /// <summary>
    /// Cree le bordereau de livraison d'une commande expediee. Le numero de
    /// commande etant unique cote livraison, un second appel renvoie le bordereau
    /// existant au lieu d'en creer un autre.
    /// </summary>
    /// <param name="order">Commande, client et lignes charges.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public Task<DeliveryCreatedResult> CreateAsync(Order order, CancellationToken cancellationToken)
    {
        var request = new CreateDeliveryRequest(
            order.Id,
            order.Number,
            order.CustomerId,
            order.Customer?.Name ?? "Client",
            order.Customer?.Phone,
            order.ShippingAddress ?? order.Customer?.Address,
            order.ShippingCity ?? order.Customer?.City,
            order.Lines
                .Select(line => new DeliveryLineRequest(line.ProductId, line.Sku, line.ProductName, line.Quantity))
                .ToList());

        return client.SendAsync<DeliveryCreatedResult>(
            ServiceName, HttpMethod.Post, "api/deliveries", request, cancellationToken);
    }
}
