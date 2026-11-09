using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Dtos;
using GestionCommandes.InventoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.InventoryService.Controllers;

/// <summary>
/// Stock par produit et par entrepot.
/// La consultation est ouverte a tout compte authentifie ; les mouvements sont
/// reserves au back-office (Gestionnaire ou Admin).
/// </summary>
[ApiController]
[Route("api/stock")]
[Produces("application/json")]
[Authorize]
public sealed class StockController(StockManager stock) : ControllerBase
{
    /// <summary>Liste paginee des lignes de stock, avec filtres.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<StockItemResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StockItemResponse>>> Search(
        [FromQuery] StockQuery query,
        CancellationToken cancellationToken)
        => Ok(await stock.SearchAsync(query, cancellationToken));

    /// <summary>Lignes de stock au niveau du seuil d'alerte ou en dessous.</summary>
    [HttpGet("low-stock")]
    [ProducesResponseType<PagedResult<StockItemResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<StockItemResponse>>> GetLowStock(
        [FromQuery] PageQuery query,
        CancellationToken cancellationToken)
        => Ok(await stock.GetLowStockAsync(query, cancellationToken));

    /// <summary>Disponibilite consolidee d'un produit, tous entrepots confondus.</summary>
    /// <param name="productId">Produit concerne.</param>
    /// <param name="warehouseId">Restreindre a un entrepot (facultatif).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{productId:guid}/availability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(
        Guid productId,
        [FromQuery] Guid? warehouseId,
        CancellationToken cancellationToken)
        => Ok(await stock.GetAvailabilityAsync(productId, warehouseId, cancellationToken));

    /// <summary>Journal pagine des mouvements de stock.</summary>
    [HttpGet("movements")]
    [ProducesResponseType<PagedResult<StockMovementResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<StockMovementResponse>>> SearchMovements(
        [FromQuery] MovementQuery query,
        CancellationToken cancellationToken)
        => Ok(await stock.SearchMovementsAsync(query, cancellationToken));

    /// <summary>
    /// Entree en stock (reception fournisseur, retour client).
    /// Cree la ligne de stock si le couple produit/entrepot est nouveau.
    /// </summary>
    /// <response code="200">Nouvel etat de la ligne de stock.</response>
    [HttpPost("entries")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<StockItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockItemResponse>> AddEntry(
        [FromBody] StockMovementRequest request,
        CancellationToken cancellationToken)
        => Ok(await stock.AddEntryAsync(request, cancellationToken));

    /// <summary>
    /// Sortie definitive hors commande (casse, perte, consommation interne).
    /// Ne peut pas entamer la part reservee aux commandes confirmees.
    /// </summary>
    /// <response code="409">Disponible insuffisant.</response>
    [HttpPost("exits")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<StockItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockItemResponse>> AddExit(
        [FromBody] StockMovementRequest request,
        CancellationToken cancellationToken)
        => Ok(await stock.AddExitAsync(request, cancellationToken));

    /// <summary>Ajustement d'inventaire : redefinit la quantite physique comptee.</summary>
    /// <response code="409">La quantite visee est inferieure a la part reservee.</response>
    [HttpPost("adjustments")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<StockItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StockItemResponse>> Adjust(
        [FromBody] StockAdjustmentRequest request,
        CancellationToken cancellationToken)
        => Ok(await stock.AdjustAsync(request, cancellationToken));

    /// <summary>Modifie le seuil d'alerte de stock bas.</summary>
    [HttpPut("{productId:guid}/warehouses/{warehouseId:guid}/threshold")]
    [Authorize(Policy = AuthPolicies.BackOffice)]
    [ProducesResponseType<StockItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockItemResponse>> SetThreshold(
        Guid productId,
        Guid warehouseId,
        [FromBody] ThresholdRequest request,
        CancellationToken cancellationToken)
        => Ok(await stock.SetThresholdAsync(productId, warehouseId, request.LowStockThreshold, cancellationToken));
}
