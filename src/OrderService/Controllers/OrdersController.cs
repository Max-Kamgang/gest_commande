using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using GestionCommandes.OrderService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.OrderService.Controllers;

/// <summary>
/// Commandes : saisie du brouillon, confirmation (saga), preparation, expedition,
/// annulation et suivi.
///
/// Un compte de role Client ne voit et ne manipule que ses propres commandes :
/// le perimetre vient du claim <c>customer_id</c> du jeton.
/// </summary>
[ApiController]
[Route("api/orders")]
[Produces("application/json")]
[Authorize]
public sealed class OrdersController(OrderManager orders, OrderSaga saga) : ControllerBase
{
    /// <summary>Liste paginee des commandes, avec filtres.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<OrderResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<OrderResponse>>> Search(
        [FromQuery] OrderQuery query,
        CancellationToken cancellationToken)
        => Ok(await orders.SearchAsync(query, cancellationToken));

    /// <summary>Recupere une commande.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await orders.GetAsync(id, cancellationToken));

    /// <summary>Recupere une commande par son numero (ex. <c>CMD-2026-00001</c>).</summary>
    [HttpGet("by-number/{number}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetByNumber(string number, CancellationToken cancellationToken)
        => Ok(await orders.GetByNumberAsync(number, cancellationToken));

    /// <summary>Historique des changements d'etat d'une commande.</summary>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType<IReadOnlyList<OrderHistoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderHistoryResponse>>> GetHistory(
        Guid id,
        CancellationToken cancellationToken)
        => Ok(await orders.GetHistoryAsync(id, cancellationToken));

    /// <summary>
    /// Cree une commande en Brouillon. Les prix ne sont pas encore figes et aucun
    /// stock n'est engage.
    /// </summary>
    /// <param name="request">Contenu de la commande.</param>
    /// <param name="idempotencyKey">
    /// En-tete <c>Idempotency-Key</c> facultatif : un rejeu de la requete renvoie
    /// la commande deja creee (200) au lieu d'en creer une seconde.
    /// </param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="201">Commande creee.</response>
    /// <response code="200">Requete rejouee : la commande existait deja pour cette cle.</response>
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = IdempotencyConstants.HeaderName)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (order, replayed) = await orders.CreateAsync(request, idempotencyKey, cancellationToken);

        if (replayed)
        {
            Response.Headers["Idempotent-Replay"] = "true";

            return Ok(order);
        }

        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Met a jour les informations d'expedition d'un brouillon.</summary>
    /// <response code="409">La commande n'est plus un brouillon.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Update(
        Guid id,
        [FromBody] UpdateOrderRequest request,
        CancellationToken cancellationToken)
        => Ok(await orders.UpdateAsync(id, request, cancellationToken));

    /// <summary>Ajoute une ligne a un brouillon (les quantites se cumulent par produit).</summary>
    [HttpPost("{id:guid}/lines")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> AddLine(
        Guid id,
        [FromBody] OrderLineRequest request,
        CancellationToken cancellationToken)
        => Ok(await orders.AddLineAsync(id, request, cancellationToken));

    /// <summary>Modifie la quantite d'une ligne de brouillon.</summary>
    [HttpPut("{id:guid}/lines/{lineId:long}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> UpdateLine(
        Guid id,
        long lineId,
        [FromBody] UpdateOrderLineRequest request,
        CancellationToken cancellationToken)
        => Ok(await orders.UpdateLineAsync(id, lineId, request, cancellationToken));

    /// <summary>Supprime une ligne de brouillon.</summary>
    [HttpDelete("{id:guid}/lines/{lineId:long}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> RemoveLine(
        Guid id,
        long lineId,
        CancellationToken cancellationToken)
        => Ok(await orders.RemoveLineAsync(id, lineId, cancellationToken));

    /// <summary>Supprime un brouillon (une commande engagee s'annule, elle ne se supprime pas).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await orders.DeleteAsync(id, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Confirme la commande : execute la saga (figement des prix depuis
    /// catalog-service puis reservation du stock dans inventory-service).
    ///
    /// En cas d'echec, la reservation eventuellement posee est liberee, la commande
    /// <b>reste en Brouillon</b> et le motif est renvoye dans le ProblemDetails
    /// comme dans le champ <c>lastError</c> de la commande.
    /// </summary>
    /// <param name="id">Identifiant de la commande.</param>
    /// <param name="idempotencyKey">En-tete <c>Idempotency-Key</c> facultatif.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    /// <response code="200">Commande confirmee.</response>
    /// <response code="409">Transition invalide, commande vide, produit inconnu/desactive, stock insuffisant.</response>
    /// <response code="503">catalog-service ou inventory-service indisponible : compensation effectuee.</response>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> Confirm(
        Guid id,
        [FromHeader(Name = IdempotencyConstants.HeaderName)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var (order, replayed) = await saga.ConfirmAsync(id, idempotencyKey, cancellationToken);

        if (replayed)
        {
            Response.Headers["Idempotent-Replay"] = "true";
        }

        return Ok(order);
    }

    /// <summary>Passe une commande confirmee en preparation. Reserve au back-office.</summary>
    /// <response code="200">Commande en preparation.</response>
    /// <response code="409">Transition invalide.</response>
    [HttpPost("{id:guid}/prepare")]
    [Authorize(Policy = AuthPolicies.BackOfficeOuService)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Prepare(Guid id, CancellationToken cancellationToken)
        => Ok(await saga.PrepareAsync(id, cancellationToken));

    /// <summary>
    /// Expedie la commande : sortie definitive du stock reserve puis creation de
    /// la livraison. Reserve au back-office.
    /// </summary>
    /// <response code="200">Commande expediee.</response>
    /// <response code="409">Transition invalide ou incoherence de stock.</response>
    /// <response code="503">inventory-service ou delivery-service indisponible : relancez l'expedition.</response>
    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = AuthPolicies.BackOfficeOuService)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> Ship(Guid id, CancellationToken cancellationToken)
        => Ok(await saga.ShipAsync(id, cancellationToken));

    /// <summary>
    /// Annule la commande. Possible tant qu'elle n'est pas expediee ; libere le
    /// stock reserve le cas echeant.
    /// </summary>
    /// <response code="200">Commande annulee.</response>
    /// <response code="409">Commande deja expediee, livree ou annulee.</response>
    /// <response code="503">inventory-service indisponible : le stock ne peut pas etre libere.</response>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> Cancel(
        Guid id,
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken)
        => Ok(await saga.CancelAsync(id, request.Reason, cancellationToken));

    /// <summary>
    /// Enregistre la livraison effective. Endpoint appele par delivery-service
    /// lorsque le livreur cloture sa livraison (appel interne de service a service).
    /// </summary>
    /// <response code="200">Commande passee a Livree (ou deja livree).</response>
    /// <response code="409">La commande n'est pas dans l'etat Expediee.</response>
    [HttpPost("{id:guid}/delivered")]
    [Authorize(Policy = AuthPolicies.BackOfficeOuService)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> MarkDelivered(
        Guid id,
        [FromBody] DeliveryCompletedNotification notification,
        CancellationToken cancellationToken)
        => Ok(await saga.MarkDeliveredAsync(id, notification, cancellationToken));

    /// <summary>Liste les etats du cycle de vie et les transitions autorisees (utile a l'interface web).</summary>
    [HttpGet("workflow")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetWorkflow() => Ok(new
    {
        states = Enum.GetValues<OrderStatus>().Select(s => new
        {
            name = s.ToString(),
            value = (int)s,
            isFinal = OrderStateMachine.IsFinal(s),
            holdsStock = OrderStateMachine.HoldsStock(s),
            allowsLineEdition = OrderStateMachine.AllowsLineEdition(s),
            transitions = OrderStateMachine.NextStates(s).Select(n => n.ToString()).ToArray()
        })
    });
}
