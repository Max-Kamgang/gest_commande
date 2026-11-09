using System.Text.Json;
using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.OrderService.Clients;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Services;

/// <summary>
/// Orchestrateur de la saga de commande. C'est order-service qui pilote : les
/// autres services sont des participants qui exposent une operation et son
/// inverse.
///
/// <b>Confirmation</b> (Brouillon → Confirmee), deux etapes :
/// <list type="number">
///   <item>recuperation des prix courants chez catalog-service, figes dans les lignes,</item>
///   <item>reservation du stock chez inventory-service, sous la reference = numero de commande.</item>
/// </list>
/// Si une etape echoue, la <b>compensation</b> libere la reservation eventuellement
/// posee et la commande <b>reste en Brouillon</b>, avec le motif de l'echec dans
/// <c>lastError</c>. Aucun etat intermediaire n'est jamais expose.
///
/// <b>Expedition</b> (EnPreparation → Expediee) : la reservation devient une
/// sortie definitive, puis la livraison est creee. Cette etape est volontairement
/// <b>rejouee en avant</b> (les deux appels sont idempotents) plutot que compensee :
/// une sortie de stock definitive ne se defait pas automatiquement.
/// </summary>
public sealed class OrderSaga(
    OrderDbContext context,
    OrderManager orders,
    ICatalogClient catalog,
    IInventoryClient inventory,
    IDeliveryClient delivery,
    IIdempotencyStore idempotency,
    ICurrentUser currentUser,
    IEventPublisher events,
    ILogger<OrderSaga> logger)
{
    /// <summary>Operation d'idempotence pour la confirmation.</summary>
    public const string ConfirmOperation = "orders.confirm";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // =======================================================================
    //  Confirmation : la saga
    // =======================================================================

    /// <summary>Confirme une commande en executant la saga de bout en bout.</summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="idempotencyKey">Cle d'idempotence facultative.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<(OrderResponse Order, bool Replayed)> ConfirmAsync(
        Guid id,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // --- Rejeu eventuel de la requete -----------------------------------
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var previous = await idempotency.FindAsync(ConfirmOperation, idempotencyKey, cancellationToken);

            if (previous is not null)
            {
                logger.LogInformation(
                    "Confirmation rejouee avec la cle d'idempotence {Key} (commande {ResourceId})",
                    idempotencyKey,
                    previous.ResourceId);

                var replayed = JsonSerializer.Deserialize<OrderResponse>(previous.ResponseBody, JsonOptions)
                               ?? throw new AppException(
                                   System.Net.HttpStatusCode.InternalServerError,
                                   "Journal d'idempotence corrompu",
                                   "La reponse enregistree pour cette cle d'idempotence est illisible.",
                                   "IDEMPOTENCY_CORRUPTED");

                return (replayed, true);
            }
        }

        var order = await orders.LoadAsync(id, tracking: true, cancellationToken);

        orders.EnsureCanAccess(order);
        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Confirmee, order.Number);

        if (order.Lines.Count == 0)
        {
            throw new ConflictException(
                $"La commande {order.Number} ne contient aucune ligne : elle ne peut pas etre confirmee.",
                "EMPTY_ORDER");
        }

        var reservationPosted = false;

        try
        {
            // --- Etape 1 : tarification -------------------------------------
            await ApplyCurrentPricesAsync(order, cancellationToken);

            // --- Etape 2 : reservation de stock -----------------------------
            var reservation = await inventory.ReserveAsync(
                new ReservationRequestDto
                {
                    Reference = order.Number,
                    Lines = order.Lines
                        .Select(l => new ReservationLineDto { ProductId = l.ProductId, Quantity = l.Quantity })
                        .ToList()
                },
                cancellationToken);

            reservationPosted = true;

            logger.LogInformation(
                "Saga {Number} : stock reserve sur {Count} emplacement(s)",
                order.Number,
                reservation.Lines.Count);

            // --- Validation de l'etat ---------------------------------------
            var actor = currentUser.UserName ?? "service-interne";

            order.Status = OrderStatus.Confirmee;
            order.ConfirmedAt = DateTimeOffset.UtcNow;
            order.LastError = null;
            order.LastErrorAt = null;
            order.AppendHistory(
                OrderStatus.Brouillon,
                OrderStatus.Confirmee,
                actor,
                "Prix figes et stock reserve");

            await SaveWithConcurrencyGuardAsync(order.Number, cancellationToken);
        }
        catch (AppException ex)
        {
            // --- Compensation ----------------------------------------------
            if (reservationPosted)
            {
                await TryCompensateAsync(order.Number, "Compensation de saga : confirmation echouee", cancellationToken);
            }
            else
            {
                // Un timeout peut avoir laisse une reservation posee cote inventory :
                // la liberation est idempotente, elle ne coute rien si rien n'existe.
                await TryCompensateAsync(order.Number, "Compensation defensive de saga", cancellationToken);
            }

            await RecordFailureAsync(id, ex.Message, cancellationToken);

            logger.LogWarning(
                "Saga {Number} echouee ({ErrorCode}) : la commande reste en Brouillon. Motif : {Motif}",
                order.Number,
                ex.ErrorCode,
                ex.Message);

            throw;
        }

        var reloaded = await orders.LoadAsync(id, tracking: false, cancellationToken);
        var response = OrderMapper.ToResponse(reloaded);

        PublishOrderEvent(
            reloaded,
            EventTypes.OrderConfirmed,
            $"Commande {reloaded.Number} confirmee",
            $"Votre commande {reloaded.Number} est confirmee pour un total de " +
            $"{reloaded.Total:N0} {reloaded.Currency} ({reloaded.Lines.Count} ligne(s)). " +
            "Le stock a ete reserve et la preparation va demarrer.",
            EventSeverities.Info);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await idempotency.SaveAsync(
                ConfirmOperation,
                idempotencyKey,
                StatusCodes.Status200OK,
                JsonSerializer.Serialize(response, JsonOptions),
                reloaded.Id.ToString(),
                cancellationToken);
        }

        logger.LogInformation(
            "Commande {Number} confirmee : total {Total} {Currency}",
            reloaded.Number,
            reloaded.Total,
            reloaded.Currency);

        return (response, false);
    }

    /// <summary>
    /// Etape 1 de la saga : recupere les prix courants et les fige dans les lignes.
    /// Un produit inconnu ou desactive fait echouer la confirmation avant toute
    /// reservation de stock.
    /// </summary>
    private async Task ApplyCurrentPricesAsync(Order order, CancellationToken cancellationToken)
    {
        var pricing = await catalog.GetPricingAsync(
            order.Lines.Select(l => l.ProductId),
            cancellationToken);

        if (pricing.Missing.Count > 0)
        {
            throw new ConflictException(
                "Confirmation impossible : les produits suivants ne figurent plus au catalogue : " +
                string.Join(", ", pricing.Missing) + ".",
                "UNKNOWN_PRODUCT",
                new Dictionary<string, object?> { ["missingProducts"] = pricing.Missing });
        }

        var byProduct = pricing.Items.ToDictionary(i => i.ProductId);

        var inactive = order.Lines
            .Where(l => byProduct.TryGetValue(l.ProductId, out var item) && !item.IsActive)
            .Select(l => byProduct[l.ProductId].Sku)
            .ToList();

        if (inactive.Count > 0)
        {
            throw new ConflictException(
                "Confirmation impossible : les produits suivants sont desactives et ne sont plus commandables : " +
                string.Join(", ", inactive) + ".",
                "PRODUCT_INACTIVE",
                new Dictionary<string, object?> { ["inactiveProducts"] = inactive });
        }

        foreach (var line in order.Lines)
        {
            var item = byProduct[line.ProductId];
            line.ApplyPrice(item.UnitPrice, item.Sku, item.Name);
        }

        order.Currency = pricing.Currency;
        order.RecomputeTotals(DemoData.VatRate);

        logger.LogInformation(
            "Saga {Number} : {Count} prix figes, total {Total} {Currency}",
            order.Number,
            order.Lines.Count,
            order.Total,
            order.Currency);
    }

    // =======================================================================
    //  Preparation
    // =======================================================================

    /// <summary>Passe une commande confirmee en preparation. Aucun appel externe.</summary>
    public async Task<OrderResponse> PrepareAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.LoadAsync(id, tracking: true, cancellationToken);

        orders.EnsureCanAccess(order);
        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.EnPreparation, order.Number);

        var actor = currentUser.UserName ?? "service-interne";

        order.Status = OrderStatus.EnPreparation;
        order.PreparedAt = DateTimeOffset.UtcNow;
        order.AppendHistory(OrderStatus.Confirmee, OrderStatus.EnPreparation, actor, "Preparation lancee");

        await SaveWithConcurrencyGuardAsync(order.Number, cancellationToken);

        logger.LogInformation("Commande {Number} passee en preparation", order.Number);

        return OrderMapper.ToResponse(order);
    }

    // =======================================================================
    //  Expedition
    // =======================================================================

    /// <summary>
    /// Expedie la commande : la reservation devient une sortie definitive de
    /// stock, puis la livraison est creee dans delivery-service.
    ///
    /// Les deux appels sont idempotents : si le second echoue, la commande reste
    /// en preparation et il suffit de relancer l'expedition (rejeu en avant).
    /// </summary>
    public async Task<OrderResponse> ShipAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.LoadAsync(id, tracking: true, cancellationToken);

        orders.EnsureCanAccess(order);
        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Expediee, order.Number);

        try
        {
            // Etape 1 : la reservation devient une sortie definitive.
            await inventory.CommitAsync(
                order.Number,
                $"Expedition de la commande {order.Number}",
                cancellationToken);

            logger.LogInformation("Commande {Number} : stock sorti definitivement", order.Number);

            // Etape 2 : creation du bordereau de livraison.
            var created = await delivery.CreateDeliveryAsync(
                new CreateDeliveryRequestDto
                {
                    OrderId = order.Id,
                    OrderNumber = order.Number,
                    CustomerId = order.CustomerId,
                    CustomerName = order.Customer?.Name ?? "Client",
                    CustomerPhone = order.Customer?.Phone,
                    ShippingAddress = order.ShippingAddress ?? order.Customer?.Address,
                    ShippingCity = order.ShippingCity ?? order.Customer?.City,
                    Lines = order.Lines.Select(l => new DeliveryLineDto
                    {
                        ProductId = l.ProductId,
                        Sku = l.Sku,
                        ProductName = l.ProductName,
                        Quantity = l.Quantity
                    }).ToList()
                },
                cancellationToken);

            var actor = currentUser.UserName ?? "service-interne";

            order.Status = OrderStatus.Expediee;
            order.ShippedAt = DateTimeOffset.UtcNow;
            order.DeliveryId = created.Id;
            order.LastError = null;
            order.LastErrorAt = null;
            order.AppendHistory(
                OrderStatus.EnPreparation,
                OrderStatus.Expediee,
                actor,
                $"Sortie de stock et livraison {created.Reference} creee");

            await SaveWithConcurrencyGuardAsync(order.Number, cancellationToken);

            logger.LogInformation(
                "Commande {Number} expediee, livraison {Reference} ({DeliveryId})",
                order.Number,
                created.Reference,
                created.Id);

            PublishOrderEvent(
                order,
                EventTypes.OrderShipped,
                $"Commande {order.Number} expediee",
                $"Votre commande {order.Number} a quitte l'entrepot. " +
                $"Le bordereau de livraison {created.Reference} a ete cree et sera affecte a un livreur.",
                EventSeverities.Info);

            return OrderMapper.ToResponse(order);
        }
        catch (AppException ex)
        {
            await RecordFailureAsync(
                id,
                $"Expedition interrompue : {ex.Message} " +
                "La commande reste en preparation ; relancez l'expedition (les operations sont idempotentes).",
                cancellationToken);

            logger.LogError(
                "Expedition de la commande {Number} interrompue ({ErrorCode}) : {Motif}",
                order.Number,
                ex.ErrorCode,
                ex.Message);

            throw;
        }
    }

    // =======================================================================
    //  Annulation
    // =======================================================================

    /// <summary>
    /// Annule une commande. Tant qu'elle n'est pas expediee, l'annulation libere
    /// la reservation de stock : c'est la meme operation de compensation que celle
    /// de la saga.
    /// </summary>
    public async Task<OrderResponse> CancelAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var order = await orders.LoadAsync(id, tracking: true, cancellationToken);

        orders.EnsureCanAccess(order);
        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Annulee, order.Number);

        var previous = order.Status;

        // Si du stock est immobilise, il DOIT etre libere avant de clore la commande.
        if (OrderStateMachine.HoldsStock(previous))
        {
            await inventory.ReleaseAsync(
                order.Number,
                $"Annulation de la commande {order.Number} : {reason}",
                cancellationToken);

            logger.LogInformation("Commande {Number} : stock libere suite a l'annulation", order.Number);
        }

        var actor = currentUser.UserName ?? "service-interne";

        order.Status = OrderStatus.Annulee;
        order.CancelledAt = DateTimeOffset.UtcNow;
        order.CancellationReason = reason.Trim();
        order.AppendHistory(previous, OrderStatus.Annulee, actor, reason.Trim());

        await SaveWithConcurrencyGuardAsync(order.Number, cancellationToken);

        logger.LogInformation(
            "Commande {Number} annulee depuis l'etat {Previous} ({Reason})",
            order.Number,
            previous,
            reason);

        PublishOrderEvent(
            order,
            EventTypes.OrderCancelled,
            $"Commande {order.Number} annulee",
            $"Votre commande {order.Number} a ete annulee. Motif : {reason.Trim()}. " +
            (OrderStateMachine.HoldsStock(previous)
                ? "Le stock reserve a ete rendu disponible."
                : "Aucun stock n'etait engage."),
            EventSeverities.Warning);

        return OrderMapper.ToResponse(order);
    }

    // =======================================================================
    //  Livraison effectuee (rappel de delivery-service)
    // =======================================================================

    /// <summary>
    /// Enregistre la livraison effective. Appele par delivery-service lorsque le
    /// livreur passe sa livraison a l'etat Livree.
    /// Idempotent : une commande deja Livree renvoie simplement son etat.
    /// </summary>
    public async Task<OrderResponse> MarkDeliveredAsync(
        Guid id,
        DeliveryCompletedNotification notification,
        CancellationToken cancellationToken)
    {
        var order = await orders.LoadAsync(id, tracking: true, cancellationToken);

        if (order.Status == OrderStatus.Livree)
        {
            logger.LogInformation("Commande {Number} deja marquee livree : reponse idempotente", order.Number);

            return OrderMapper.ToResponse(order);
        }

        OrderStateMachine.EnsureCanTransition(order.Status, OrderStatus.Livree, order.Number);

        var actor = currentUser.UserName ?? "delivery-service";

        order.Status = OrderStatus.Livree;
        order.DeliveredAt = notification.DeliveredAt ?? DateTimeOffset.UtcNow;
        order.DeliveryId ??= notification.DeliveryId;
        order.AppendHistory(
            OrderStatus.Expediee,
            OrderStatus.Livree,
            actor,
            string.IsNullOrWhiteSpace(notification.ReceivedBy)
                ? "Livraison confirmee par le livreur"
                : $"Receptionnee par {notification.ReceivedBy}");

        await SaveWithConcurrencyGuardAsync(order.Number, cancellationToken);

        logger.LogInformation(
            "Commande {Number} livree le {DeliveredAt}",
            order.Number,
            order.DeliveredAt);

        PublishOrderEvent(
            order,
            EventTypes.OrderDelivered,
            $"Commande {order.Number} livree",
            $"Votre commande {order.Number} a ete livree" +
            (string.IsNullOrWhiteSpace(notification.ReceivedBy)
                ? "."
                : $" et receptionnee par {notification.ReceivedBy}.") +
            " Merci de votre confiance.",
            EventSeverities.Info);

        return OrderMapper.ToResponse(order);
    }

    // =======================================================================
    //  Mecanique interne
    // =======================================================================

    /// <summary>
    /// Compensation : libere la reservation de stock. Ne leve jamais d'exception,
    /// afin de ne pas masquer l'erreur d'origine de la saga.
    /// </summary>
    private async Task TryCompensateAsync(string reference, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var released = await inventory.ReleaseAsync(reference, reason, cancellationToken);

            logger.LogInformation(
                "Compensation effectuee pour {Reference} : reservation {Status}",
                reference,
                released.Status);
        }
        catch (NotFoundException)
        {
            // Aucune reservation posee : il n'y avait rien a compenser.
            logger.LogInformation(
                "Compensation pour {Reference} : aucune reservation a liberer",
                reference);
        }
        catch (Exception ex)
        {
            // La compensation a echoue : on le journalise clairement pour que
            // l'exploitant puisse liberer manuellement via l'API inventory.
            logger.LogError(
                ex,
                "ECHEC DE COMPENSATION pour {Reference} : la reservation de stock doit etre liberee manuellement " +
                "(DELETE /api/reservations/{Reference} sur inventory-service)",
                reference,
                reference);
        }
    }

    /// <summary>
    /// Enregistre le motif d'echec sur la commande, sans valider les modifications
    /// partielles de la saga : le suivi des changements est vide puis la commande
    /// est rechargee depuis la base.
    /// </summary>
    private async Task RecordFailureAsync(Guid orderId, string message, CancellationToken cancellationToken)
    {
        try
        {
            context.ChangeTracker.Clear();

            var fresh = await context.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (fresh is null)
            {
                return;
            }

            fresh.LastError = message.Length > 1000 ? message[..1000] : message;
            fresh.LastErrorAt = DateTimeOffset.UtcNow;

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impossible d'enregistrer le motif d'echec de la commande {OrderId}", orderId);
        }
    }

    /// <summary>
    /// Enregistre les modifications en traduisant un conflit de concurrence
    /// optimiste en 409 explicite : deux transitions simultanees sur la meme
    /// commande ne doivent pas produire un 500.
    /// </summary>
    private async Task SaveWithConcurrencyGuardAsync(string orderNumber, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            context.ChangeTracker.Clear();

            logger.LogWarning(ex, "Conflit de concurrence sur la commande {Number}", orderNumber);

            throw new ConflictException(
                $"La commande {orderNumber} vient d'etre modifiee par une autre operation. " +
                "Rechargez-la puis reessayez.",
                "CONCURRENCY_CONFLICT");
        }
    }

    /// <summary>Publie un evenement de commande vers notification-service (fire-and-forget).</summary>
    private void PublishOrderEvent(
        Order order,
        string eventType,
        string title,
        string message,
        string severity)
        => events.Publish(new IntegrationEvent
        {
            EventType = eventType,
            Subject = order.Number,
            Title = title,
            Message = message,
            Severity = severity,
            Channel = EventChannels.Email,
            Recipient = order.Customer?.Email,
            Data = new Dictionary<string, string>
            {
                ["orderId"] = order.Id.ToString(),
                ["orderNumber"] = order.Number,
                ["customerId"] = order.CustomerId.ToString(),
                ["status"] = order.Status.ToString(),
                ["total"] = order.Total.ToString("0.##"),
                ["currency"] = order.Currency,
                ["lineCount"] = order.Lines.Count.ToString()
            }
        });
}
