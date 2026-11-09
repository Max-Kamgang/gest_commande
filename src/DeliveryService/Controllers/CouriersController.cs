using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.DeliveryService.Dtos;
using GestionCommandes.DeliveryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.DeliveryService.Controllers;

/// <summary>Livreurs. Gestion reservee au back-office ; la consultation est ouverte aux livreurs.</summary>
[ApiController]
[Route("api/couriers")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.Livraisons)]
public sealed class CouriersController(CourierManager couriers) : ControllerBase
{
    /// <summary>Liste paginee des livreurs, avec leur charge courante.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CourierResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CourierResponse>>> Search(
        [FromQuery] CourierQuery query,
        CancellationToken cancellationToken)
        => Ok(await couriers.SearchAsync(query, cancellationToken));

    /// <summary>Recupere un livreur.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CourierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourierResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await couriers.GetAsync(id, cancellationToken));

    /// <summary>Cree un livreur. Reserve au back-office.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CourierResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourierResponse>> Create(
        [FromBody] CourierRequest request,
        CancellationToken cancellationToken)
    {
        var created = await couriers.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour un livreur. Reserve au back-office.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CourierResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CourierResponse>> Update(
        Guid id,
        [FromBody] CourierRequest request,
        CancellationToken cancellationToken)
        => Ok(await couriers.UpdateAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive un livreur. Reserve au back-office.</summary>
    /// <response code="409">Le livreur a encore des livraisons en charge.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CourierResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourierResponse>> SetStatus(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
        => Ok(await couriers.SetActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>Supprime un livreur sans aucune livraison. Reserve au back-office.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await couriers.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
