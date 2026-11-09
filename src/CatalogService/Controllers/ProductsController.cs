using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.CatalogService.Dtos;
using GestionCommandes.CatalogService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.CatalogService.Controllers;

/// <summary>Produits du catalogue.</summary>
[ApiController]
[Route("api/products")]
[Produces("application/json")]
[Authorize]
public sealed class ProductsController(CatalogManager catalog) : ControllerBase
{
    /// <summary>
    /// Liste paginee des produits, avec recherche, filtres (categorie, activation,
    /// fourchette de prix) et tri.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductResponse>>> Search(
        [FromQuery] ProductQuery query,
        CancellationToken cancellationToken)
        => Ok(await catalog.SearchProductsAsync(query, cancellationToken));

    /// <summary>Recupere un produit.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await catalog.GetProductAsync(id, cancellationToken));

    /// <summary>Recupere un produit par sa reference article.</summary>
    [HttpGet("by-sku/{sku}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetBySku(string sku, CancellationToken cancellationToken)
        => Ok(await catalog.GetProductBySkuAsync(sku, cancellationToken));

    /// <summary>Historique des changements de prix d'un produit.</summary>
    [HttpGet("{id:guid}/price-history")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<IReadOnlyList<PriceHistoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PriceHistoryResponse>>> GetPriceHistory(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await catalog.GetPriceHistoryAsync(id, cancellationToken));

    /// <summary>Cree un produit. Reserve au back-office.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> Create(
        [FromBody] CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var created = await catalog.CreateProductAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Met a jour un produit (hors prix). Reserve au back-office.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        [FromBody] UpdateProductRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.UpdateProductAsync(id, request, cancellationToken));

    /// <summary>Revalorise un produit et trace le changement. Reserve au back-office.</summary>
    /// <response code="409">Le produit est deja a ce prix.</response>
    [HttpPatch("{id:guid}/price")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductResponse>> ChangePrice(
        Guid id,
        [FromBody] ChangePriceRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.ChangePriceAsync(id, request, cancellationToken));

    /// <summary>Active ou desactive un produit. Reserve au back-office.</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductResponse>> SetStatus(
        Guid id,
        [FromBody] SetActiveRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.SetProductActiveAsync(id, request.IsActive, cancellationToken));

    /// <summary>Supprime un produit. Reserve au back-office.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await catalog.DeleteProductAsync(id, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Renvoie les prix courants d'une liste de produits.
    /// Endpoint consomme par order-service lors de la confirmation d'une commande :
    /// il est accessible au back-office, aux clients et aux appels internes de service.
    /// </summary>
    /// <response code="200">Prix courants et liste des produits inconnus.</response>
    [HttpPost("pricing")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Gestionnaire + "," + Roles.Client + "," + Roles.Service)]
    [ProducesResponseType<PricingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PricingResponse>> GetPricing(
        [FromBody] PricingRequest request,
        CancellationToken cancellationToken)
        => Ok(await catalog.GetPricingAsync(request, cancellationToken));
}
