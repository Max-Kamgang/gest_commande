using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Domain;
using GestionCommandes.InventoryService.Dtos;
using GestionCommandes.InventoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.InventoryService.Controllers;

/// <summary>
/// Reservations de stock : participant de la saga de commande.
/// Ces endpoints sont appeles par order-service (cle interne) ou par le
/// back-office pour une intervention manuelle.
/// </summary>
[ApiController]
[Route("api/reservations")]
[Produces("application/json")]
[Authorize(Policy = AuthPolicies.BackOfficeOuService)]
public sealed class ReservationsController(ReservationManager reservations) : ControllerBase
{
    /// <summary>Liste paginee des reservations.</summary>
    /// <param name="query">Pagination.</param>
    /// <param name="status">Filtre d'etat (<c>Active</c>, <c>Liberee</c>, <c>Consommee</c>).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReservationResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> Search(
        [FromQuery] PageQuery query,
        [FromQuery] ReservationStatus? status,
        CancellationToken cancellationToken)
        => Ok(await reservations.SearchAsync(query, status, cancellationToken));

    /// <summary>Recupere une reservation par sa reference (numero de commande).</summary>
    [HttpGet("{reference}")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> Get(string reference, CancellationToken cancellationToken)
        => Ok(await reservations.GetAsync(reference, cancellationToken));

    /// <summary>
    /// Reserve le stock d'une commande (etape 2 de la saga de confirmation).
    /// Operation atomique : soit toutes les lignes sont reservees, soit aucune.
    /// Idempotente sur la reference.
    /// </summary>
    /// <response code="201">Reservation posee.</response>
    /// <response code="200">Reservation deja active : reponse idempotente.</response>
    /// <response code="409">Stock insuffisant (detail dans <c>shortages</c>) ou conflit de concurrence.</response>
    [HttpPost]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Reserve(
        [FromBody] ReservationRequest request,
        CancellationToken cancellationToken)
    {
        var (reservation, alreadyExisted) = await reservations.ReserveAsync(request, cancellationToken);

        return alreadyExisted
            ? Ok(reservation)
            : CreatedAtAction(nameof(Get), new { reference = reservation.Reference }, reservation);
    }

    /// <summary>
    /// Libere une reservation : le stock redevient disponible.
    /// C'est l'action de compensation de la saga. Idempotente.
    /// </summary>
    /// <param name="reference">Numero de commande.</param>
    /// <param name="reason">Motif journalise (facultatif).</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="200">Reservation liberee.</response>
    /// <response code="409">Reservation deja consommee par une expedition.</response>
    [HttpDelete("{reference}")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Release(
        string reference,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
        => Ok(await reservations.ReleaseAsync(reference, reason, cancellationToken));

    /// <summary>
    /// Consomme une reservation : le stock reserve devient une sortie definitive.
    /// Appele au passage de la commande a l'etat Expediee. Idempotente.
    /// </summary>
    /// <response code="200">Reservation consommee.</response>
    /// <response code="409">Reservation deja liberee.</response>
    [HttpPost("{reference}/commit")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Commit(
        string reference,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
        => Ok(await reservations.CommitAsync(reference, reason, cancellationToken));
}
