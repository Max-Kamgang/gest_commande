using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.CatalogService.Dtos;
using GestionCommandes.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.CatalogService.Controllers;

/// <summary>Categories du catalogue.</summary>
[ApiController]
[Route("api/categories")]
[Produces("application/json")]
[Authorize]
public sealed class CategoriesController(CatalogManager catalog) : ControllerBase
{
    /// <summary>Liste paginee des categories.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CategoryResponse>>> Search(
        [FromQuery] CategoryQuery query,
        CancellationToken cancellationToken)
        => Ok(await catalog.SearchCategoriesAsync(query, cancellationToken));

    /// <summary>Recupere une categorie.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await catalog.GetCategoryAsync(id, cancellationToken));

    /// <summary>Cree une categorie. Reserve au back-office.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CategoryResponse>> Create(
        [FromBody] CategoryRequest request,
        CancellationToken cancellationToken)
    {
        var created = await catalog.CreateCategoryAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour une categorie. Reserve au back-office.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id,
        [FromBody] CategoryRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.UpdateCategoryAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive une categorie. Reserve au back-office.</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<CategoryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryResponse>> SetStatus(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.SetCategoryActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>Supprime une categorie vide. Reserve au back-office.</summary>
    /// <response code="409">La categorie contient encore des produits.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await catalog.DeleteCategoryAsync(id, cancellationToken);

        return NoContent();
    }
}
