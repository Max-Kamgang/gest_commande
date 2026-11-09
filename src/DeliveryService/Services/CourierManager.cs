using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Services;

/// <summary>Gestion des livreurs.</summary>
public sealed class CourierManager(DeliveryDbContext context, ILogger<CourierManager> logger)
{
    /// <summary>Etats consideres comme « en charge » pour un livreur.</summary>
    private static readonly DeliveryStatus[] BusyStates =
        [DeliveryStatus.Affectee, DeliveryStatus.EnCours, DeliveryStatus.Echouee];

    /// <summary>Liste paginee des livreurs, avec leur charge courante.</summary>
    public async Task<PagedResult<CourierResponse>> SearchAsync(
        CourierQuery query,
        CancellationToken cancellationToken)
    {
        var source = context.Couriers
            .AsNoTracking()
            .Include(c => c.Deliveries)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c =>
                EF.Functions.Like(c.Code, $"%{term}%") ||
                EF.Functions.Like(c.FullName, $"%{term}%") ||
                EF.Functions.Like(c.Email, $"%{term}%") ||
                (c.Zone != null && EF.Functions.Like(c.Zone, $"%{term}%")));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(c => c.IsActive == isActive);
        }

        return await source
            .OrderBy(c => c.Code)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere un livreur.</summary>
    public async Task<CourierResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .AsNoTracking()
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le livreur", id);

        return ToResponse(courier);
    }

    /// <summary>Cree un livreur.</summary>
    public async Task<CourierResponse> CreateAsync(CourierRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await context.Couriers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Un livreur porte deja le code '{code}'.", "COURIER_CODE_TAKEN");
        }

        if (await context.Couriers.AnyAsync(c => c.Email == email, cancellationToken))
        {
            throw new ConflictException($"Un livreur utilise deja l'adresse '{email}'.", "COURIER_EMAIL_TAKEN");
        }

        var courier = new Courier
        {
            Code = code,
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = request.Phone?.Trim(),
            Vehicle = request.Vehicle.Trim(),
            Zone = request.Zone?.Trim(),
            MaxConcurrentDeliveries = request.MaxConcurrentDeliveries,
            IsActive = true
        };

        context.Couriers.Add(courier);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Livreur {Code} ({Name}) cree", code, courier.FullName);

        return ToResponse(courier);
    }

    /// <summary>Met a jour un livreur.</summary>
    public async Task<CourierResponse> UpdateAsync(
        Guid id,
        CourierRequest request,
        CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le livreur", id);

        var code = request.Code.Trim().ToUpperInvariant();
        var email = request.Email.Trim().ToLowerInvariant();

        if (await context.Couriers.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            throw new ConflictException($"Un autre livreur porte deja le code '{code}'.", "COURIER_CODE_TAKEN");
        }

        if (await context.Couriers.AnyAsync(c => c.Email == email && c.Id != id, cancellationToken))
        {
            throw new ConflictException($"Un autre livreur utilise deja l'adresse '{email}'.", "COURIER_EMAIL_TAKEN");
        }

        courier.Code = code;
        courier.FullName = request.FullName.Trim();
        courier.Email = email;
        courier.Phone = request.Phone?.Trim();
        courier.Vehicle = request.Vehicle.Trim();
        courier.Zone = request.Zone?.Trim();
        courier.MaxConcurrentDeliveries = request.MaxConcurrentDeliveries;
        courier.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(courier);
    }

    /// <summary>Active ou desactive un livreur. Un livreur charge ne peut pas etre desactive.</summary>
    public async Task<CourierResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le livreur", id);

        if (!isActive)
        {
            var busy = courier.Deliveries.Count(d => BusyStates.Contains(d.Status));

            if (busy > 0)
            {
                throw new ConflictException(
                    $"Le livreur '{courier.Code}' a encore {busy} livraison(s) en charge. " +
                    "Reaffectez-les avant de le desactiver.",
                    "COURIER_HAS_ACTIVE_DELIVERIES",
                    new Dictionary<string, object?> { ["activeDeliveries"] = busy });
            }
        }

        courier.IsActive = isActive;
        courier.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Livreur {Code} {Etat}", courier.Code, isActive ? "active" : "desactive");

        return ToResponse(courier);
    }

    /// <summary>Supprime un livreur sans aucune livraison.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le livreur", id);

        if (courier.Deliveries.Count > 0)
        {
            throw new ConflictException(
                $"Le livreur '{courier.Code}' est rattache a {courier.Deliveries.Count} livraison(s) : " +
                "desactivez-le au lieu de le supprimer, afin de conserver l'historique.",
                "COURIER_HAS_DELIVERIES",
                new Dictionary<string, object?> { ["deliveryCount"] = courier.Deliveries.Count });
        }

        context.Couriers.Remove(courier);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Livreur {Code} supprime", courier.Code);
    }

    /// <summary>Verifie qu'un livreur peut recevoir une nouvelle affectation.</summary>
    public async Task<Courier> GetAssignableAsync(Guid courierId, CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == courierId, cancellationToken)
            ?? throw new BadRequestException($"Le livreur '{courierId}' est inconnu.", "UNKNOWN_COURIER");

        if (!courier.IsActive)
        {
            throw new ConflictException(
                $"Le livreur '{courier.Code}' est desactive : aucune affectation possible.",
                "COURIER_INACTIVE");
        }

        var busy = courier.Deliveries.Count(d => BusyStates.Contains(d.Status));

        if (busy >= courier.MaxConcurrentDeliveries)
        {
            throw new ConflictException(
                $"Le livreur '{courier.Code}' a atteint sa charge maximale " +
                $"({busy}/{courier.MaxConcurrentDeliveries} livraisons en cours).",
                "COURIER_AT_CAPACITY",
                new Dictionary<string, object?>
                {
                    ["activeDeliveries"] = busy,
                    ["maxConcurrentDeliveries"] = courier.MaxConcurrentDeliveries
                });
        }

        return courier;
    }

    internal static CourierResponse ToResponse(Courier courier) => new()
    {
        Id = courier.Id,
        Code = courier.Code,
        FullName = courier.FullName,
        Email = courier.Email,
        Phone = courier.Phone,
        Vehicle = courier.Vehicle,
        Zone = courier.Zone,
        MaxConcurrentDeliveries = courier.MaxConcurrentDeliveries,
        IsActive = courier.IsActive,
        ActiveDeliveries = courier.Deliveries.Count(d => BusyStates.Contains(d.Status)),
        CompletedDeliveries = courier.Deliveries.Count(d => d.Status == DeliveryStatus.Livree),
        CreatedAt = courier.CreatedAt,
        UpdatedAt = courier.UpdatedAt
    };
}
