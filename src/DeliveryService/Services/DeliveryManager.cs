using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.DeliveryService.Clients;
using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Services;

/// <summary>
/// Gestion des livraisons : creation (par order-service), affectation d'un
/// livreur, suivi des statuts et notification de la commande a la livraison.
///
/// Regle d'autorisation importante : un compte de role <b>Livreur</b> ne peut
/// consulter et faire evoluer que <b>ses</b> livraisons. Le rattachement se fait
/// par le claim <c>courier_id</c> du jeton, jamais par un parametre de requete.
/// </summary>
public sealed class DeliveryManager(
    DeliveryDbContext context,
    CourierManager couriers,
    IOrderClient orderClient,
    ICurrentUser currentUser,
    IEventPublisher events,
    ILogger<DeliveryManager> logger)
{
    /// <summary>Liste paginee des livraisons, filtree selon le role de l'appelant.</summary>
    public async Task<PagedResult<DeliveryResponse>> SearchAsync(
        DeliveryQuery query,
        CancellationToken cancellationToken)
    {
        if (query.From is { } from && query.To is { } to && from > to)
        {
            throw new BadRequestException("from ne peut pas etre posterieur a to.", "INVALID_DATE_RANGE");
        }

        var source = context.Deliveries
            .AsNoTracking()
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .AsQueryable();

        // Un livreur ne voit que ses propres livraisons.
        var scopedCourierId = ResolveCourierScope(query.CourierId);

        if (scopedCourierId is { } courierId)
        {
            source = source.Where(d => d.CourierId == courierId);
        }

        if (query.Status is { } status)
        {
            source = source.Where(d => d.Status == status);
        }

        if (query.UnassignedOnly == true)
        {
            source = source.Where(d => d.CourierId == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(d =>
                EF.Functions.Like(d.Reference, $"%{term}%") ||
                EF.Functions.Like(d.OrderNumber, $"%{term}%") ||
                EF.Functions.Like(d.CustomerName, $"%{term}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.City))
        {
            var city = query.City.Trim();
            source = source.Where(d => d.ShippingCity != null && EF.Functions.Like(d.ShippingCity, $"%{city}%"));
        }

        if (query.From is { } fromDate)
        {
            source = source.Where(d => d.CreatedAt >= fromDate);
        }

        if (query.To is { } toDate)
        {
            source = source.Where(d => d.CreatedAt <= toDate);
        }

        source = query.Sort?.ToLowerInvariant() switch
        {
            "createdat" => source.OrderBy(d => d.CreatedAt),
            "reference" => source.OrderBy(d => d.Reference),
            "reference_desc" => source.OrderByDescending(d => d.Reference),
            _ => source.OrderByDescending(d => d.CreatedAt)
        };

        return await source.ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere une livraison.</summary>
    public async Task<DeliveryResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: false, cancellationToken);

        EnsureCanAccess(delivery);

        return ToResponse(delivery);
    }

    /// <summary>Recupere la livraison d'une commande.</summary>
    public async Task<DeliveryResponse> GetByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var normalized = orderNumber.Trim().ToUpperInvariant();

        var delivery = await context.Deliveries
            .AsNoTracking()
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.OrderNumber == normalized, cancellationToken)
            ?? throw new NotFoundException("La livraison de la commande", orderNumber);

        EnsureCanAccess(delivery);

        return ToResponse(delivery);
    }

    /// <summary>Historique de suivi d'une livraison.</summary>
    public async Task<IReadOnlyList<TrackingResponse>> GetTrackingAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: false, cancellationToken);

        EnsureCanAccess(delivery);

        return await context.Tracking
            .AsNoTracking()
            .Where(t => t.DeliveryId == id)
            .OrderBy(t => t.ChangedAt)
            .ThenBy(t => t.Id)
            .Select(t => new TrackingResponse
            {
                FromStatus = t.FromStatus,
                ToStatus = t.ToStatus,
                Location = t.Location,
                Comment = t.Comment,
                ChangedBy = t.ChangedBy,
                ChangedAt = t.ChangedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Cree un bordereau de livraison. Appele par order-service a l'expedition.
    /// Idempotent sur le numero de commande : un second appel renvoie le
    /// bordereau existant, ce qui permet a order-service de rejouer l'etape.
    /// </summary>
    public async Task<(DeliveryResponse Delivery, bool AlreadyExisted)> CreateAsync(
        CreateDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var orderNumber = request.OrderNumber.Trim().ToUpperInvariant();

        var existing = await context.Deliveries
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.OrderNumber == orderNumber, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "Livraison de la commande {OrderNumber} deja creee ({Reference}) : reponse idempotente",
                orderNumber,
                existing.Reference);

            return (ToResponse(existing), true);
        }

        var delivery = new Delivery
        {
            Reference = await NextReferenceAsync(cancellationToken),
            OrderId = request.OrderId,
            OrderNumber = orderNumber,
            CustomerId = request.CustomerId,
            CustomerName = request.CustomerName.Trim(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            ShippingAddress = request.ShippingAddress?.Trim(),
            ShippingCity = request.ShippingCity?.Trim(),
            Notes = request.Notes?.Trim(),
            Status = DeliveryStatus.EnAttente,
            Lines = request.Lines.Select(l => new DeliveryLine
            {
                ProductId = l.ProductId,
                Sku = l.Sku?.Trim(),
                ProductName = l.ProductName?.Trim(),
                Quantity = l.Quantity
            }).ToList()
        };

        delivery.AppendTracking(
            null,
            DeliveryStatus.EnAttente,
            currentUser.UserName ?? "order-service",
            $"Bordereau cree a l'expedition de la commande {orderNumber}");

        context.Deliveries.Add(delivery);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Livraison {Reference} creee pour la commande {OrderNumber} ({Lines} ligne(s))",
            delivery.Reference,
            orderNumber,
            delivery.Lines.Count);

        return (ToResponse(delivery), false);
    }

    /// <summary>Affecte (ou reaffecte) un livreur. Reserve au back-office.</summary>
    public async Task<DeliveryResponse> AssignAsync(
        Guid id,
        AssignCourierRequest request,
        CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: true, cancellationToken);

        if (!DeliveryStateMachine.AllowsAssignment(delivery.Status))
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} est dans l'etat « {delivery.Status} » : " +
                "aucune affectation de livreur n'est possible.",
                "ASSIGNMENT_NOT_ALLOWED",
                new Dictionary<string, object?> { ["currentStatus"] = delivery.Status.ToString() });
        }

        var courier = await couriers.GetAssignableAsync(request.CourierId, cancellationToken);

        if (delivery.CourierId == courier.Id && delivery.Status == DeliveryStatus.Affectee)
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} est deja affectee au livreur '{courier.Code}'.",
                "ALREADY_ASSIGNED");
        }

        var previous = delivery.Status;
        var previousCourier = delivery.CourierId;

        delivery.CourierId = courier.Id;
        delivery.Courier = courier;
        delivery.Status = DeliveryStatus.Affectee;
        delivery.AssignedAt = DateTimeOffset.UtcNow;

        delivery.AppendTracking(
            previous,
            DeliveryStatus.Affectee,
            currentUser.UserName ?? "back-office",
            request.Comment ?? (previousCourier is null
                ? $"Affectee a {courier.FullName} ({courier.Vehicle})"
                : $"Reaffectee a {courier.FullName} ({courier.Vehicle})"));

        await SaveWithConcurrencyGuardAsync(delivery.Reference, cancellationToken);

        logger.LogInformation(
            "Livraison {Reference} affectee au livreur {Courier}",
            delivery.Reference,
            courier.Code);

        events.Publish(new IntegrationEvent
        {
            EventType = EventTypes.DeliveryAssigned,
            Subject = delivery.Reference,
            Title = $"Livraison {delivery.Reference} affectee",
            Message =
                $"La livraison {delivery.Reference} (commande {delivery.OrderNumber}) a ete affectee a " +
                $"{courier.FullName}. Destination : {delivery.ShippingCity ?? "non precisee"}.",
            Severity = EventSeverities.Info,
            Channel = EventChannels.Sms,
            Recipient = courier.Phone,
            Data = BuildEventData(delivery)
        });

        return ToResponse(delivery);
    }

    /// <summary>
    /// Fait evoluer le statut d'une livraison.
    ///
    /// Un livreur ne peut agir que sur ses propres livraisons. Le passage a
    /// <c>Livree</c> exige le nom du destinataire et declenche la notification
    /// d'order-service, qui passe alors la commande a l'etat Livree.
    /// </summary>
    public async Task<DeliveryResponse> UpdateStatusAsync(
        Guid id,
        UpdateDeliveryStatusRequest request,
        CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: true, cancellationToken);

        EnsureCanAccess(delivery);
        EnsureCanUpdate(delivery);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, request.Status, delivery.Reference);

        if (request.Status is DeliveryStatus.EnCours or DeliveryStatus.Livree && delivery.CourierId is null)
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} n'a pas de livreur affecte : " +
                "affectez un livreur avant de demarrer la tournee.",
                "COURIER_REQUIRED");
        }

        var previous = delivery.Status;
        var actor = currentUser.UserName ?? "back-office";
        var now = DateTimeOffset.UtcNow;

        switch (request.Status)
        {
            case DeliveryStatus.EnCours:
                delivery.PickedUpAt ??= now;
                break;

            case DeliveryStatus.Livree:
                if (string.IsNullOrWhiteSpace(request.ReceivedBy))
                {
                    throw new BadRequestException(
                        "receivedBy est obligatoire pour cloturer une livraison : indiquez le nom du destinataire.",
                        "RECEIVED_BY_REQUIRED");
                }

                delivery.DeliveredAt = now;
                delivery.ReceivedBy = request.ReceivedBy.Trim();
                delivery.FailureReason = null;
                break;

            case DeliveryStatus.Echouee:
                if (string.IsNullOrWhiteSpace(request.FailureReason))
                {
                    throw new BadRequestException(
                        "failureReason est obligatoire pour declarer un echec de livraison.",
                        "FAILURE_REASON_REQUIRED");
                }

                delivery.FailedAt = now;
                delivery.FailureReason = request.FailureReason.Trim();
                delivery.AttemptCount++;
                break;

            case DeliveryStatus.Affectee:
            case DeliveryStatus.EnAttente:
            case DeliveryStatus.Annulee:
            default:
                break;
        }

        delivery.Status = request.Status;
        delivery.AppendTracking(previous, request.Status, actor, request.Comment, request.Location);

        await SaveWithConcurrencyGuardAsync(delivery.Reference, cancellationToken);

        logger.LogInformation(
            "Livraison {Reference} : {Previous} -> {Status} par {Actor}",
            delivery.Reference,
            previous,
            request.Status,
            actor);

        // La cloture declenche le rappel vers order-service.
        if (request.Status == DeliveryStatus.Livree)
        {
            await NotifyOrderDeliveredAsync(delivery, cancellationToken);

            events.Publish(new IntegrationEvent
            {
                EventType = EventTypes.DeliveryCompleted,
                Subject = delivery.Reference,
                Title = $"Livraison {delivery.Reference} effectuee",
                Message =
                    $"La commande {delivery.OrderNumber} a ete livree a {delivery.CustomerName} " +
                    $"et receptionnee par {delivery.ReceivedBy}" +
                    (string.IsNullOrWhiteSpace(delivery.ShippingCity) ? "." : $" a {delivery.ShippingCity}.") +
                    (delivery.AttemptCount > 0 ? $" ({delivery.AttemptCount} tentative(s) precedente(s))" : string.Empty),
                Severity = EventSeverities.Info,
                Channel = EventChannels.Email,
                Recipient = "gestion@micro.local",
                Data = BuildEventData(delivery)
            });
        }

        return ToResponse(delivery);
    }

    /// <summary>Annule une livraison. Reserve au back-office.</summary>
    public async Task<DeliveryResponse> CancelAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: true, cancellationToken);

        DeliveryStateMachine.EnsureCanTransition(delivery.Status, DeliveryStatus.Annulee, delivery.Reference);

        var previous = delivery.Status;

        delivery.Status = DeliveryStatus.Annulee;
        delivery.CancelledAt = DateTimeOffset.UtcNow;
        delivery.AppendTracking(
            previous,
            DeliveryStatus.Annulee,
            currentUser.UserName ?? "back-office",
            reason.Trim());

        await SaveWithConcurrencyGuardAsync(delivery.Reference, cancellationToken);

        logger.LogWarning(
            "Livraison {Reference} annulee depuis l'etat {Previous} ({Reason})",
            delivery.Reference,
            previous,
            reason);

        return ToResponse(delivery);
    }

    // =======================================================================
    //  Mecanique interne
    // =======================================================================

    /// <summary>
    /// Informe order-service que la livraison est effectuee. Un echec n'annule
    /// pas la cloture : la livraison reste Livree, le drapeau <c>orderNotified</c>
    /// reste faux et le probleme est journalise pour reprise.
    /// </summary>
    private async Task NotifyOrderDeliveredAsync(Delivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await orderClient.NotifyDeliveredAsync(
                delivery.OrderId,
                new DeliveryCompletedDto
                {
                    DeliveryId = delivery.Id,
                    DeliveredAt = delivery.DeliveredAt,
                    ReceivedBy = delivery.ReceivedBy
                },
                cancellationToken);

            delivery.OrderNotified = true;
            await context.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "order-service informe : la commande {OrderNumber} passe a Livree",
                delivery.OrderNumber);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Impossible d'informer order-service de la livraison {Reference} (commande {OrderNumber}). " +
                "La livraison reste cloturee ; relancez la notification via " +
                "POST /api/deliveries/{DeliveryId}/notify-order",
                delivery.Reference,
                delivery.OrderNumber,
                delivery.Id);
        }
    }

    /// <summary>Relance la notification d'order-service pour une livraison deja cloturee.</summary>
    public async Task<DeliveryResponse> RetryOrderNotificationAsync(Guid id, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(id, tracking: true, cancellationToken);

        if (delivery.Status != DeliveryStatus.Livree)
        {
            throw new ConflictException(
                $"La livraison {delivery.Reference} n'est pas cloturee (etat « {delivery.Status} ») : " +
                "il n'y a rien a notifier.",
                "DELIVERY_NOT_COMPLETED");
        }

        if (delivery.OrderNotified)
        {
            logger.LogInformation(
                "Livraison {Reference} : order-service est deja informe",
                delivery.Reference);

            return ToResponse(delivery);
        }

        // Ici l'erreur doit remonter : l'appel est explicitement demande par l'exploitant.
        await orderClient.NotifyDeliveredAsync(
            delivery.OrderId,
            new DeliveryCompletedDto
            {
                DeliveryId = delivery.Id,
                DeliveredAt = delivery.DeliveredAt,
                ReceivedBy = delivery.ReceivedBy
            },
            cancellationToken);

        delivery.OrderNotified = true;
        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(delivery);
    }

    private async Task<Delivery> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = context.Deliveries
            .Include(d => d.Courier)
            .Include(d => d.Lines)
            .AsQueryable();

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
               ?? throw new NotFoundException("La livraison", id);
    }

    /// <summary>Un livreur n'accede qu'a ses propres livraisons.</summary>
    private void EnsureCanAccess(Delivery delivery)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return;
        }

        if (currentUser.IsInRole(Roles.Livreur) && currentUser.CourierId == delivery.CourierId)
        {
            return;
        }

        throw new ForbiddenException(
            $"Votre compte n'a pas acces a la livraison {delivery.Reference}. " +
            "Un livreur ne consulte que les livraisons qui lui sont affectees.");
    }

    /// <summary>Un livreur ne modifie que les livraisons qui lui sont affectees.</summary>
    private void EnsureCanUpdate(Delivery delivery)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return;
        }

        if (!currentUser.IsInRole(Roles.Livreur))
        {
            throw new ForbiddenException("Seuls un livreur, un gestionnaire ou un administrateur peuvent faire evoluer une livraison.");
        }

        if (delivery.CourierId is null || currentUser.CourierId != delivery.CourierId)
        {
            throw new ForbiddenException(
                $"La livraison {delivery.Reference} ne vous est pas affectee : vous ne pouvez pas la mettre a jour.");
        }
    }

    private Guid? ResolveCourierScope(Guid? requested)
    {
        if (currentUser.IsBackOffice || currentUser.IsInRole(Roles.Service))
        {
            return requested;
        }

        if (currentUser.IsInRole(Roles.Livreur))
        {
            return currentUser.CourierId
                   ?? throw new ForbiddenException(
                       "Votre compte n'est rattache a aucun livreur : aucune livraison ne peut etre listee. " +
                       "Un administrateur doit renseigner courierId sur votre compte.");
        }

        throw new ForbiddenException("Votre role ne permet pas de consulter les livraisons.");
    }

    private async Task SaveWithConcurrencyGuardAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            context.ChangeTracker.Clear();

            logger.LogWarning(ex, "Conflit de concurrence sur la livraison {Reference}", reference);

            throw new ConflictException(
                $"La livraison {reference} vient d'etre modifiee par une autre operation. " +
                "Rechargez-la puis reessayez.",
                "CONCURRENCY_CONFLICT");
        }
    }

    /// <summary>Attribue la prochaine reference de livraison (<c>LIV-2026-00001</c>).</summary>
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

    private static Dictionary<string, string> BuildEventData(Delivery delivery) => new()
    {
        ["deliveryId"] = delivery.Id.ToString(),
        ["reference"] = delivery.Reference,
        ["orderId"] = delivery.OrderId.ToString(),
        ["orderNumber"] = delivery.OrderNumber,
        ["customerId"] = delivery.CustomerId.ToString(),
        ["status"] = delivery.Status.ToString(),
        ["courierId"] = delivery.CourierId?.ToString() ?? string.Empty,
        ["city"] = delivery.ShippingCity ?? string.Empty
    };

    internal static DeliveryResponse ToResponse(Delivery delivery) => new()
    {
        Id = delivery.Id,
        Reference = delivery.Reference,
        OrderId = delivery.OrderId,
        OrderNumber = delivery.OrderNumber,
        CustomerId = delivery.CustomerId,
        CustomerName = delivery.CustomerName,
        CustomerPhone = delivery.CustomerPhone,
        ShippingAddress = delivery.ShippingAddress,
        ShippingCity = delivery.ShippingCity,
        Status = delivery.Status,
        AllowedTransitions = DeliveryStateMachine.NextStates(delivery.Status).ToList(),
        CourierId = delivery.CourierId,
        CourierName = delivery.Courier?.FullName,
        CourierPhone = delivery.Courier?.Phone,
        Notes = delivery.Notes,
        ReceivedBy = delivery.ReceivedBy,
        FailureReason = delivery.FailureReason,
        AttemptCount = delivery.AttemptCount,
        OrderNotified = delivery.OrderNotified,
        CreatedAt = delivery.CreatedAt,
        AssignedAt = delivery.AssignedAt,
        PickedUpAt = delivery.PickedUpAt,
        DeliveredAt = delivery.DeliveredAt,
        FailedAt = delivery.FailedAt,
        CancelledAt = delivery.CancelledAt,
        Lines = delivery.Lines
            .OrderBy(l => l.Id)
            .Select(l => new DeliveryLineResponse
            {
                Id = l.Id,
                ProductId = l.ProductId,
                Sku = l.Sku,
                ProductName = l.ProductName,
                Quantity = l.Quantity
            })
            .ToList()
    };
}
