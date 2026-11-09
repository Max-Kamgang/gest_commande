using GestionCommandes.Shared.Errors;

namespace GestionCommandes.OrderService.Domain;

/// <summary>
/// Etats du cycle de vie d'une commande.
///
/// Chemin nominal : Brouillon, Confirmee, EnPreparation, Expediee, Livree.
/// L'annulation reste possible tant que la marchandise n'est pas sortie du stock.
/// </summary>
public enum OrderStatus
{
    /// <summary>En cours de saisie : lignes modifiables, aucun stock engage.</summary>
    Brouillon = 1,

    /// <summary>Prix figes et stock reserve : la commande est engagee.</summary>
    Confirmee = 2,

    /// <summary>Preparation physique en cours.</summary>
    EnPreparation = 3,

    /// <summary>Stock sorti et bordereau de livraison cree.</summary>
    Expediee = 4,

    /// <summary>Livraison confirmee par le livreur. Etat final.</summary>
    Livree = 5,

    /// <summary>Commande annulee, stock libere. Etat final.</summary>
    Annulee = 6
}

/// <summary>
/// Machine a etats de la commande, volontairement separee de l'infrastructure :
/// elle ne connait ni la base ni HTTP, et se lit comme la regle metier.
///
/// Regle du projet : une transition interdite renvoie un 409 avec le detail des
/// transitions possibles, jamais un 400 ni un 500.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        [OrderStatus.Brouillon] = [OrderStatus.Confirmee, OrderStatus.Annulee],
        [OrderStatus.Confirmee] = [OrderStatus.EnPreparation, OrderStatus.Annulee],
        [OrderStatus.EnPreparation] = [OrderStatus.Expediee, OrderStatus.Annulee],
        [OrderStatus.Expediee] = [OrderStatus.Livree],
        [OrderStatus.Livree] = [],
        [OrderStatus.Annulee] = []
    };

    /// <summary>Etats dans lesquels du stock est immobilise par une reservation.</summary>
    public static readonly OrderStatus[] StatesHoldingStock = [OrderStatus.Confirmee, OrderStatus.EnPreparation];

    /// <summary>Indique si une transition est autorisee.</summary>
    /// <param name="from">Etat courant.</param>
    /// <param name="to">Etat demande.</param>
    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Etats accessibles depuis un etat donne.</summary>
    /// <param name="from">Etat courant.</param>
    public static IReadOnlyCollection<OrderStatus> NextStates(OrderStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Indique si l'etat est final.</summary>
    /// <param name="status">Etat a tester.</param>
    public static bool IsFinal(OrderStatus status) => NextStates(status).Count == 0;

    /// <summary>Indique si les lignes sont encore modifiables.</summary>
    /// <param name="status">Etat a tester.</param>
    public static bool AllowsLineEdition(OrderStatus status) => status == OrderStatus.Brouillon;

    /// <summary>Indique si du stock est reserve dans cet etat.</summary>
    /// <param name="status">Etat a tester.</param>
    public static bool HoldsStock(OrderStatus status) => StatesHoldingStock.Contains(status);

    /// <summary>Verifie une transition, ou leve un 409 explicite.</summary>
    /// <param name="from">Etat courant.</param>
    /// <param name="to">Etat demande.</param>
    /// <param name="orderNumber">Numero de commande, pour le message.</param>
    public static void EnsureCanTransition(OrderStatus from, OrderStatus to, string orderNumber)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        var next = NextStates(from);

        throw new ConflictException(IsFinal(from)
            ? $"La commande {orderNumber} est dans l'etat final « {from} » : aucune transition n'est plus " +
              $"possible (transition demandee : « {to} »)."
            : $"La commande {orderNumber} est dans l'etat « {from} » : la transition vers « {to} » n'est pas " +
              $"autorisee. Transition(s) possible(s) : {string.Join(", ", next)}.");
    }
}
