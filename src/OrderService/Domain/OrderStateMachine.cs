using GestionCommandes.BuildingBlocks.Errors;

namespace GestionCommandes.OrderService.Domain;

/// <summary>
/// Machine a etats de la commande, isolee de l'infrastructure pour etre
/// directement testable unitairement.
///
/// Regle du projet : toute transition non autorisee se traduit par un
/// <b>409 Conflict</b> porteur d'un message explicite, jamais par un 400 ou un 500.
/// </summary>
public static class OrderStateMachine
{
    /// <summary>Transitions autorisees, etat par etat.</summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        [OrderStatus.Brouillon] = [OrderStatus.Confirmee, OrderStatus.Annulee],
        [OrderStatus.Confirmee] = [OrderStatus.EnPreparation, OrderStatus.Annulee],
        [OrderStatus.EnPreparation] = [OrderStatus.Expediee, OrderStatus.Annulee],
        [OrderStatus.Expediee] = [OrderStatus.Livree],
        [OrderStatus.Livree] = [],
        [OrderStatus.Annulee] = []
    };

    /// <summary>Etats finaux : plus aucune transition n'est possible.</summary>
    public static readonly OrderStatus[] FinalStates = [OrderStatus.Livree, OrderStatus.Annulee];

    /// <summary>Etats depuis lesquels une annulation est encore recevable.</summary>
    public static readonly OrderStatus[] CancellableStates =
        [OrderStatus.Brouillon, OrderStatus.Confirmee, OrderStatus.EnPreparation];

    /// <summary>Etats dans lesquels du stock est immobilise par une reservation.</summary>
    public static readonly OrderStatus[] StatesHoldingStock =
        [OrderStatus.Confirmee, OrderStatus.EnPreparation];

    /// <summary>Indique si une transition est autorisee.</summary>
    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Etats accessibles depuis un etat donne.</summary>
    public static IReadOnlyCollection<OrderStatus> NextStates(OrderStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Indique si l'etat est final.</summary>
    public static bool IsFinal(OrderStatus status) => FinalStates.Contains(status);

    /// <summary>Indique si les lignes de la commande sont encore modifiables.</summary>
    public static bool AllowsLineEdition(OrderStatus status) => status == OrderStatus.Brouillon;

    /// <summary>Indique si du stock est actuellement reserve pour cet etat.</summary>
    public static bool HoldsStock(OrderStatus status) => StatesHoldingStock.Contains(status);

    /// <summary>
    /// Verifie une transition et leve un <see cref="ConflictException"/> explicite
    /// (409) si elle n'est pas autorisee.
    /// </summary>
    /// <param name="from">Etat courant.</param>
    /// <param name="to">Etat demande.</param>
    /// <param name="orderNumber">Numero de commande, pour le message d'erreur.</param>
    public static void EnsureCanTransition(OrderStatus from, OrderStatus to, string orderNumber)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        var next = NextStates(from);

        var detail = IsFinal(from)
            ? $"La commande {orderNumber} est dans l'etat final « {from} » : aucune transition n'est plus possible " +
              $"(transition demandee : « {to} »)."
            : $"La commande {orderNumber} est dans l'etat « {from} » : la transition vers « {to} » n'est pas " +
              $"autorisee. Transition(s) possible(s) : {string.Join(", ", next)}.";

        throw new ConflictException(
            detail,
            "INVALID_STATE_TRANSITION",
            new Dictionary<string, object?>
            {
                ["currentStatus"] = from.ToString(),
                ["requestedStatus"] = to.ToString(),
                ["allowedTransitions"] = next.Select(s => s.ToString()).ToArray()
            });
    }
}
