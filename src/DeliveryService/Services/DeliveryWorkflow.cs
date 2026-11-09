using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Services;

/// <summary>
/// Appel de delivery-service vers order-service : prevenir que la commande est livree.
/// C'est le seul appel sortant de ce service.
/// </summary>
public sealed class OrderClient(HttpClient client)
{
    /// <summary>Signale a order-service que la commande a ete remise au client.</summary>
    /// <param name="orderId">Identifiant de la commande.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public Task MarkDeliveredAsync(Guid orderId, CancellationToken cancellationToken) =>
        client.SendAsync(
            "order-service", HttpMethod.Post, $"api/orders/{orderId}/delivered", null, cancellationToken);
}

/// <summary>
/// Cycle de vie d'une livraison : creation du bordereau puis affectation,
/// enlevement, remise ou echec.
///
/// Chaque changement d'etat produit deux traces : une etape de suivi sur le
/// bordereau, et une notification lisible par le back-office.
/// </summary>
public sealed class DeliveryWorkflow(
    DeliveryDbContext context,
    OrderClient orders,
    ICurrentUser currentUser,
    ILogger<DeliveryWorkflow> logger)
{
    /// <summary>
    /// Cree le bordereau d'une commande expediee. Si le numero de commande est
    /// deja connu, renvoie le bordereau existant : order-service peut rappeler
    /// sans creer de doublon.
    /// </summary>
    /// <param name="request">Donnees de la commande expediee.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<(Delivery Delivery, bool Created)> CreateAsync(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var orderNumber = request.OrderNumber.Trim();

        var existing = await LoadByOrderNumberAsync(orderNumber, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Bordereau deja existant pour la commande {Commande} : {Reference}",
                orderNumber, existing.Reference);

            return (existing, false);
        }

        var delivery = new Delivery
        {
            Reference = await NextReferenceAsync(cancellationToken),
            OrderId = request.OrderId,
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone,
            ShippingAddress = request.ShippingAddress,
            ShippingCity = request.ShippingCity,
            Lines = request.Lines.Select(line => new DeliveryLine
            {
                ProductId = line.ProductId,
                Sku = line.Sku,
                ProductName = line.ProductName,
                Quantity = line.Quantity
            }).ToList()
        };

        delivery.AppendTracking(
            null, DeliveryStatus.EnAttente, currentUser.UserName, $"Bordereau cree pour {orderNumber}");

        context.Deliveries.Add(delivery);

        Notify(
            "delivery.created",
            delivery.Reference,
            $"Livraison {delivery.Reference} a preparer",
            $"La commande {orderNumber} de {delivery.CustomerName} attend l'affectation d'un livreur " +
            $"({delivery.ShippingCity ?? "ville non precisee"}).");

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Bordereau {Reference} cree pour la commande {Commande}", delivery.Reference, orderNumber);

        return (delivery, true);
    }

    /// <summary>Affecte un livreur a un bordereau.</summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="courierId">Livreur a affecter.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> AssignAsync(Guid deliveryId, Guid courierId, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(deliveryId, cancellationToken);

        if (!DeliveryStateMachine.AllowsAssignment(delivery.Status))
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} est « {delivery.Status} » : elle n'accepte plus d'affectation.");
        }

        var courier = await context.Couriers.FirstOrDefaultAsync(c => c.Id == courierId, cancellationToken)
                      ?? throw new NotFoundException("Le livreur", courierId);

        if (!courier.IsActive)
        {
            throw new ConflictException($"Le livreur {courier.Code} est inactif : il ne peut pas etre affecte.");
        }

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.Affectee, delivery.Reference);

        delivery.CourierId = courier.Id;
        delivery.AppendTracking(
            delivery.Status, DeliveryStatus.Affectee, currentUser.UserName, $"Affectee a {courier.FullName}");
        delivery.Status = DeliveryStatus.Affectee;
        delivery.AssignedAt = DateTimeOffset.UtcNow;

        Notify(
            "delivery.assigned",
            delivery.Reference,
            $"Livraison {delivery.Reference} affectee",
            $"{courier.FullName} ({courier.Vehicle}) prend en charge la commande {delivery.OrderNumber}.");

        await context.SaveChangesAsync(cancellationToken);

        return delivery;
    }

    /// <summary>Enregistre l'enlevement : la livraison part en tournee.</summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="comment">Commentaire du livreur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> PickUpAsync(Guid deliveryId, string? comment, CancellationToken cancellationToken)
    {
        var delivery = await LoadForCourierAsync(deliveryId, cancellationToken);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.EnCours, delivery.Reference);

        delivery.AppendTracking(delivery.Status, DeliveryStatus.EnCours, currentUser.UserName, comment);
        delivery.Status = DeliveryStatus.EnCours;
        delivery.PickedUpAt = DateTimeOffset.UtcNow;

        Notify(
            "delivery.picked-up",
            delivery.Reference,
            $"Livraison {delivery.Reference} en route",
            $"La commande {delivery.OrderNumber} est en cours d'acheminement vers {delivery.CustomerName}.");

        await context.SaveChangesAsync(cancellationToken);

        return delivery;
    }

    /// <summary>
    /// Enregistre la remise au client, puis previent order-service pour que la
    /// commande passe a l'etat Livree.
    /// </summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="receivedBy">Personne ayant receptionne.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> CompleteAsync(
        Guid deliveryId,
        string receivedBy,
        CancellationToken cancellationToken)
    {
        var delivery = await LoadForCourierAsync(deliveryId, cancellationToken);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.Livree, delivery.Reference);

        delivery.AppendTracking(
            delivery.Status, DeliveryStatus.Livree, currentUser.UserName, $"Recu par {receivedBy}");
        delivery.Status = DeliveryStatus.Livree;
        delivery.DeliveredAt = DateTimeOffset.UtcNow;
        delivery.ReceivedBy = receivedBy.Trim();

        Notify(
            "delivery.completed",
            delivery.Reference,
            $"Livraison {delivery.Reference} effectuee",
            $"La commande {delivery.OrderNumber} a ete remise a {delivery.ReceivedBy}.");

        // La livraison est enregistree AVANT de prevenir order-service : le travail
        // du livreur ne doit pas etre perdu parce qu'un autre conteneur ne repond pas.
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            await orders.MarkDeliveredAsync(delivery.OrderId, cancellationToken);

            delivery.OrderNotified = true;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // La commande restera « Expediee » jusqu'au prochain appel reussi.
            logger.LogError(
                ex,
                "Livraison {Reference} enregistree mais order-service n'a pas pu etre prevenu. " +
                "Relancer POST /api/deliveries/{Id}/notify-order.",
                delivery.Reference,
                delivery.Id);

            Notify(
                "delivery.order-not-notified",
                delivery.Reference,
                $"Commande {delivery.OrderNumber} non mise a jour",
                "La livraison est enregistree ici, mais order-service n'a pas repondu : la commande est " +
                "encore marquee « Expediee ». Relancer la notification depuis la fiche de livraison.",
                NotificationSeverity.Avertissement);

            await context.SaveChangesAsync(cancellationToken);
        }

        return delivery;
    }

    /// <summary>
    /// Reessaie de prevenir order-service d'une livraison deja enregistree.
    /// </summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> NotifyOrderAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(deliveryId, cancellationToken);

        if (delivery.Status != DeliveryStatus.Livree)
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} n'est pas livree : il n'y a rien a signaler a order-service.");
        }

        await orders.MarkDeliveredAsync(delivery.OrderId, cancellationToken);

        delivery.OrderNotified = true;
        await context.SaveChangesAsync(cancellationToken);

        return delivery;
    }

    /// <summary>Enregistre une tentative infructueuse.</summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="reason">Motif de l'echec.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> FailAsync(Guid deliveryId, string reason, CancellationToken cancellationToken)
    {
        var delivery = await LoadForCourierAsync(deliveryId, cancellationToken);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.Echouee, delivery.Reference);

        delivery.AppendTracking(delivery.Status, DeliveryStatus.Echouee, currentUser.UserName, reason);
        delivery.Status = DeliveryStatus.Echouee;
        delivery.FailureReason = reason.Trim();
        delivery.AttemptCount++;

        Notify(
            "delivery.failed",
            delivery.Reference,
            $"Echec de la livraison {delivery.Reference}",
            $"Tentative n°{delivery.AttemptCount} pour la commande {delivery.OrderNumber} : {delivery.FailureReason}. " +
            "Le bordereau peut etre reaffecte.",
            NotificationSeverity.Avertissement);

        await context.SaveChangesAsync(cancellationToken);

        return delivery;
    }

    /// <summary>Annule un bordereau.</summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="comment">Motif de l'annulation.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> CancelAsync(Guid deliveryId, string? comment, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(deliveryId, cancellationToken);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.Annulee, delivery.Reference);

        delivery.AppendTracking(delivery.Status, DeliveryStatus.Annulee, currentUser.UserName, comment);
        delivery.Status = DeliveryStatus.Annulee;

        Notify(
            "delivery.cancelled",
            delivery.Reference,
            $"Livraison {delivery.Reference} annulee",
            $"Le bordereau de la commande {delivery.OrderNumber} a ete annule. {comment}".Trim(),
            NotificationSeverity.Avertissement);

        await context.SaveChangesAsync(cancellationToken);

        return delivery;
    }

    /// <summary>Charge un bordereau avec son livreur, ses lignes et son suivi.</summary>
    /// <param name="deliveryId">Identifiant du bordereau.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public async Task<Delivery> LoadAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        await context.Deliveries
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .Include(d => d.Tracking)
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken)
        ?? throw new NotFoundException("La livraison", deliveryId);

    /// <summary>
    /// Charge un bordereau en verifiant qu'un livreur n'agit que sur les siens.
    /// </summary>
    private async Task<Delivery> LoadForCourierAsync(Guid deliveryId, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(deliveryId, cancellationToken);

        if (currentUser.IsBackOffice)
        {
            return delivery;
        }

        var courierId = currentUser.CourierId
                        ?? throw new ForbiddenException(
                            "Votre compte n'est rattache a aucun livreur : demandez a un administrateur " +
                            "de renseigner courierId.");

        if (delivery.CourierId != courierId)
        {
            throw new ForbiddenException(
                $"La livraison {delivery.Reference} n'est pas affectee a votre compte.");
        }

        return delivery;
    }

    private Task<Delivery?> LoadByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken) =>
        context.Deliveries
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .Include(d => d.Tracking)
            .FirstOrDefaultAsync(d => d.OrderNumber == orderNumber, cancellationToken);

    /// <summary>Attribue la prochaine reference de bordereau de l'annee.</summary>
    private async Task<string> NextReferenceAsync(CancellationToken cancellationToken)
    {
        var year = DateTimeOffset.UtcNow.Year;

        var sequence = await context.DeliverySequences.FirstOrDefaultAsync(s => s.Year == year, cancellationToken);

        if (sequence is null)
        {
            sequence = new DeliverySequence { Year = year, LastValue = 0 };
            context.DeliverySequences.Add(sequence);
        }

        sequence.LastValue++;

        return $"LIV-{year}-{sequence.LastValue:D5}";
    }

    /// <summary>
    /// Ajoute une notification a enregistrer avec le reste de l'operation :
    /// elle part dans le meme SaveChanges, donc jamais sans son changement d'etat.
    /// </summary>
    private void Notify(
        string eventType,
        string subject,
        string title,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info)
        => context.Notifications.Add(new Notification
        {
            EventType = eventType,
            Subject = subject,
            Title = title,
            Message = message,
            Severity = severity
        });
}
