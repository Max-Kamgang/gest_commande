using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.OrderService.Dtos;
using GestionCommandes.OrderService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.OrderService.Controllers;

/// <summary>Clients. Gestion reservee au back-office.</summary>
[ApiController]
[Route("api/customers")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.BackOfficeOuService)]
public sealed class CustomersController(CustomerManager customers) : ControllerBase
{
    /// <summary>Liste paginee des clients.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> Search(
        [FromQuery] CustomerQuery query,
        CancellationToken cancellationToken)
        => Ok(await customers.SearchAsync(query, cancellationToken));

    /// <summary>Recupere un client.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await customers.GetAsync(id, cancellationToken));

    /// <summary>Cree un client.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerResponse>> Create(
        [FromBody] CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var created = await customers.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour un client.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        [FromBody] CustomerRequest request,
        CancellationToken cancellationToken)
        => Ok(await customers.UpdateAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive un client.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<CustomerResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerResponse>> SetStatus(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
        => Ok(await customers.SetActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>Supprime un client sans commande.</summary>
    /// <response code="409">Le client porte des commandes : desactivez-le plutot.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await customers.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
