using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Dtos;
using GestionCommandes.InventoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.InventoryService.Controllers;

/// <summary>Entrepots.</summary>
[ApiController]
[Route("api/warehouses")]
[Produces("application/json")]
[Authorize]
public sealed class WarehousesController(WarehouseManager warehouses) : ControllerBase
{
    /// <summary>Liste paginee des entrepots.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<WarehouseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<WarehouseResponse>>> Search(
        [FromQuery] WarehouseQuery query,
        CancellationToken cancellationToken)
        => Ok(await warehouses.SearchAsync(query, cancellationToken));

    /// <summary>Recupere un entrepot.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await warehouses.GetAsync(id, cancellationToken));

    /// <summary>Cree un entrepot. Reserve au back-office.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarehouseResponse>> Create(
        [FromBody] WarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var created = await warehouses.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour un entrepot. Reserve au back-office.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WarehouseResponse>> Update(
        Guid id,
        [FromBody] WarehouseRequest request,
        CancellationToken cancellationToken)
        => Ok(await warehouses.UpdateAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive un entrepot. Reserve au back-office.</summary>
    /// <response code="409">Des reservations sont encore actives sur cet entrepot.</response>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<WarehouseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WarehouseResponse>> SetStatus(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
        => Ok(await warehouses.SetActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>Supprime un entrepot vide. Reserve au back-office.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await warehouses.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}

/// <summary>Activation / desactivation.</summary>
public sealed class SetActiveRequest
{
    /// <summary>Nouvel etat.</summary>
    public bool IsActive { get; set; }
}
