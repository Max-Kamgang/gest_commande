using GestionCommandes.DeliveryService.Data;
using GestionCommandes.DeliveryService.Domain;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.DeliveryService.Controllers;

/// <summary>Livreurs. Consultation ouverte au back-office et aux livreurs, gestion reservee au back-office.</summary>
[ApiController]
[Route("api/couriers")]
[Authorize(Policy = Policies.Livraisons)]
public sealed class CouriersController(DeliveryDbContext context) : ControllerBase
{
    /// <summary>Liste les livreurs, avec leur charge en cours.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CourierResponse>>> List(CancellationToken cancellationToken)
    {
        var couriers = await context.Couriers
            .OrderBy(c => c.Code)
            .Select(c => new CourierResponse
            {
                Id = c.Id,
                Code = c.Code,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                Vehicle = c.Vehicle,
                Zone = c.Zone,
                IsActive = c.IsActive,
                ActiveDeliveries = c.Deliveries.Count(d =>
                    d.Status == DeliveryStatus.Affectee || d.Status == DeliveryStatus.EnCours)
            })
            .ToListAsync(cancellationToken);

        return Ok(couriers);
    }

    /// <summary>Renvoie un livreur.</summary>
    /// <param name="id">Identifiant du livreur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CourierResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await DescribeAsync(id, cancellationToken));

    /// <summary>Cree un livreur.</summary>
    /// <param name="request">Donnees du livreur.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CourierResponse>> Create(
        CourierRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Couriers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Le livreur '{code}' existe deja.");
        }

        var courier = new Courier
        {
            Code = code,
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            Vehicle = string.IsNullOrWhiteSpace(request.Vehicle) ? "Moto" : request.Vehicle.Trim(),
            Zone = request.Zone?.Trim()
        };

        context.Couriers.Add(courier);
        await context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = courier.Id }, await DescribeAsync(courier.Id, cancellationToken));
    }

    /// <summary>Met a jour un livreur.</summary>
    /// <param name="id">Identifiant du livreur.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CourierResponse>> Update(
        Guid id,
        CourierRequest request,
        CancellationToken cancellationToken)
    {
        var courier = await context.Couriers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Le livreur", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Couriers.AnyAsync(c => c.Id != id && c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Le livreur '{code}' existe deja.");
        }

        courier.Code = code;
        courier.FullName = request.FullName.Trim();
        courier.Email = request.Email.Trim().ToLowerInvariant();
        courier.Phone = request.Phone?.Trim();
        courier.Vehicle = string.IsNullOrWhiteSpace(request.Vehicle) ? courier.Vehicle : request.Vehicle.Trim();
        courier.Zone = request.Zone?.Trim();

        await context.SaveChangesAsync(cancellationToken);

        return Ok(await DescribeAsync(id, cancellationToken));
    }

    /// <summary>Active ou desactive un livreur.</summary>
    /// <param name="id">Identifiant du livreur.</param>
    /// <param name="isActive">Nouvel etat.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CourierResponse>> SetActive(
        Guid id,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken)
    {
        var courier = await context.Couriers
            .Include(c => c.Deliveries)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le livreur", id);

        // On ne desactive pas un livreur qui a encore des livraisons en main :
        // elles deviendraient orphelines sans que personne ne s'en apercoive.
        if (!isActive)
        {
            var enCours = courier.Deliveries.Count(d =>
                d.Status is DeliveryStatus.Affectee or DeliveryStatus.EnCours);

            if (enCours > 0)
            {
                throw new ConflictException(
                    $"Le livreur {courier.Code} a encore {enCours} livraison(s) en cours : " +
                    "reaffectez-les avant de le desactiver.");
            }
        }

        courier.IsActive = isActive;
        await context.SaveChangesAsync(cancellationToken);

        return Ok(await DescribeAsync(id, cancellationToken));
    }

    /// <summary>Projection partagee : un livreur et sa charge en cours.</summary>
    private async Task<CourierResponse> DescribeAsync(Guid id, CancellationToken cancellationToken) =>
        await context.Couriers
            .Where(c => c.Id == id)
            .Select(c => new CourierResponse
            {
                Id = c.Id,
                Code = c.Code,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                Vehicle = c.Vehicle,
                Zone = c.Zone,
                IsActive = c.IsActive,
                ActiveDeliveries = c.Deliveries.Count(d =>
                    d.Status == DeliveryStatus.Affectee || d.Status == DeliveryStatus.EnCours)
            })
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException("Le livreur", id);
}
