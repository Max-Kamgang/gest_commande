using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.Shared.Domain;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Services;

/// <summary>
/// Enchainement des etapes d'une commande. C'est le seul endroit du projet qui
/// coordonne plusieurs services, et c'est volontaire : order-service pilote,
/// catalog-service et delivery-service se contentent d'executer.
///
/// Deux regles tiennent tout l'ensemble :
///
/// 1. <b>Si une etape distante echoue, on revient en arriere.</b> A la
///    confirmation, si le stock est reserve mais que l'enregistrement local
///    echoue, la reservation est liberee et la commande RESTE en brouillon, avec
///    le motif dans <c>lastError</c>. Aucun etat a moitie confirme n'existe.
///
/// 2. <b>A l'expedition, on rejoue en avant plutot que d'annuler.</b> Une sortie
///    de stock definitive ne se reprend pas automatiquement : si la creation du
///    bordereau echoue apres la sortie, la commande reste en preparation et il
///    suffit de relancer l'expedition. Les deux appels distants sont rejouables,
///    donc la relance ne double rien.
/// </summary>
public sealed class OrderWorkflow(
    OrderDbContext context,
    CatalogClient catalog,
    DeliveryClient delivery,
    ICurrentUser currentUser,
    ILogger<OrderWorkflow> logger)
{
    /// <summary>
    /// Confirme une commande : reservation du stock, figement des prix, calcul des totaux.
    /// </summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> ConfirmAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Confirmee, order.Number);

        if (order.Lines.Count == 0)
        {
            throw new BadRequestException(
                $"La commande {order.Number} n'a aucune ligne : ajoutez au moins un produit avant de confirmer.");
        }

        // --- Etape distante : stock reserve et prix recuperes en un appel -----
        ReservationResult reservation;

        try
        {
            reservation = await catalog.ReserveAsync(order, cancellationToken);
        }
        catch (AppException ex)
        {
            // Stock insuffisant, produit desactive, catalogue injoignable : la
            // commande reste en brouillon et le client peut lire pourquoi.
            order.LastError = ex.Message;
            await context.SaveChangesAsync(cancellationToken);

            logger.LogWarning("Confirmation de {Numero} refusee : {Motif}", order.Number, ex.Message);

            throw;
        }

        // --- Etape locale, avec retour en arriere si elle echoue --------------
        try
        {
            foreach (var line in order.Lines)
            {
                var priced = reservation.Lines.FirstOrDefault(l => l.ProductId == line.ProductId)
                             ?? throw new ConflictException(
                                 $"Le catalogue n'a pas renvoye de prix pour le produit {line.ProductId} " +
                                 $"de la commande {order.Number}.");

                line.ApplyPrice(priced.UnitPrice, priced.Sku, priced.ProductName);
            }

            order.Currency = reservation.Currency;
            order.RecomputeTotals(DemoData.VatRate);

            order.AppendHistory(order.Status, OrderStatus.Confirmee, currentUser.UserName);
            order.Status = OrderStatus.Confirmee;
            order.ConfirmedAt = DateTimeOffset.UtcNow;
            order.LastError = null;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Echec de l'enregistrement de la confirmation de {Numero} : liberation de la reservation.",
                order.Number);

            // Retour en arriere : le stock est rendu, la commande reste en brouillon.
            await TryReleaseAsync(order.Number, cancellationToken);

            throw;
        }

        logger.LogInformation(
            "Commande {Numero} confirmee : {Total} {Devise}", order.Number, order.Total, order.Currency);

        return order;
    }

    /// <summary>Passe une commande confirmee en preparation. Aucune etape distante.</summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> StartPreparationAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.EnPreparation, order.Number);

        order.AppendHistory(order.Status, OrderStatus.EnPreparation, currentUser.UserName);
        order.Status = OrderStatus.EnPreparation;

        await context.SaveChangesAsync(cancellationToken);

        return order;
    }

    /// <summary>
    /// Expedie une commande : sortie definitive du stock puis creation du
    /// bordereau de livraison.
    /// </summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> ShipAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Expediee, order.Number);

        // 1. La reservation devient une sortie definitive.
        await catalog.ShipAsync(order.Number, cancellationToken);

        // 2. Le bordereau est cree chez delivery-service. Si cet appel echoue, la
        //    commande reste en preparation : relancer l'expedition reprend ici,
        //    les deux appels etant rejouables.
        var created = await delivery.CreateAsync(order, cancellationToken);

        order.DeliveryId = created.Id;
        order.AppendHistory(order.Status, OrderStatus.Expediee, currentUser.UserName, $"Bordereau {created.Reference}");
        order.Status = OrderStatus.Expediee;
        order.ShippedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Commande {Numero} expediee, bordereau {Reference}", order.Number, created.Reference);

        return order;
    }

    /// <summary>
    /// Annule une commande. Si du stock etait immobilise, il est libere avant
    /// l'enregistrement : on ne garde jamais une commande annulee qui retient du stock.
    /// </summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="reason">Motif d'annulation.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> CancelAsync(Guid orderId, string? reason, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Annulee, order.Number);

        if (OrderStateMachine.HoldsStock(order.Status))
        {
            await catalog.ReleaseAsync(order.Number, cancellationToken);
        }

        order.AppendHistory(order.Status, OrderStatus.Annulee, currentUser.UserName, reason);
        order.Status = OrderStatus.Annulee;
        order.CancelledAt = DateTimeOffset.UtcNow;
        order.CancellationReason = reason;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Commande {Numero} annulee : {Motif}", order.Number, reason ?? "sans motif");

        return order;
    }

    /// <summary>
    /// Marque une commande comme livree. Appele par delivery-service quand le
    /// livreur confirme la remise au client.
    /// </summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> MarkDeliveredAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await LoadAsync(orderId, cancellationToken);

        // Rejeu : delivery-service peut rappeler si sa premiere notification a echoue.
        if (order.Status == OrderStatus.Livree)
        {
            return order;
        }

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Livree, order.Number);

        order.AppendHistory(order.Status, OrderStatus.Livree, currentUser.UserName);
        order.Status = OrderStatus.Livree;
        order.DeliveredAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Commande {Numero} livree.", order.Number);

        return order;
    }

    /// <summary>
    /// Attribue le prochain numero de commande de l'annee en cours
    /// (<c>CMD-2026-00001</c>), via un compteur en base.
    /// </summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<string> NextNumberAsync(CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;

        var sequence = await context.OrderSequences.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new OrderSequence { Year = year, LastValue = 0 };
            context.OrderSequences.Add(sequence);
        }

        sequence.LastValue++;

        return $"CMD-{year}-{sequence.LastValue:D5}";
    }

    /// <summary>Charge une commande avec son client, ses lignes et son historique.</summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Order> LoadAsync(Guid orderId, CancellationToken cancellationToken) =>
        await context.Orders
            .Include(o => o.Customer)
            .Include(o => o.Lines)
            .Include(o => o.History)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
        ?? throw new NotFoundException("La commande", orderId);

    /// <summary>
    /// Libere une reservation sans propager d'erreur : on est deja dans un
    /// traitement d'echec, et l'erreur d'origine est celle qui compte pour
    /// l'appelant. L'echec de liberation est journalise pour l'exploitant.
    /// </summary>
    private async Task TryReleaseAsync(string orderNumber, CancellationToken cancellationToken)
    {
        try
        {
            await catalog.ReleaseAsync(orderNumber, cancellationToken);

            logger.LogInformation("Reservation de {Numero} liberee.", orderNumber);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "La reservation de {Numero} n'a pas pu etre liberee. Le stock reste immobilise : " +
                "liberer manuellement via POST /api/stock/reservations/{Numero}/release.",
                orderNumber,
                orderNumber);
        }
    }
}
