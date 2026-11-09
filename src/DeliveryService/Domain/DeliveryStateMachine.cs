using GestionCommandes.BuildingBlocks.Errors;

namespace GestionCommandes.DeliveryService.Domain;

/// <summary>
/// Machine a etats de la livraison, isolee pour etre testable unitairement.
/// Une tentative echouee est reaffectable : c'est le cas reel du client absent.
/// </summary>
public static class DeliveryStateMachine
{
    private static readonly Dictionary<DeliveryStatus, DeliveryStatus[]> Transitions = new()
    {
        [DeliveryStatus.EnAttente] = [DeliveryStatus.Affectee, DeliveryStatus.Annulee],
        [DeliveryStatus.Affectee] = [DeliveryStatus.EnCours, DeliveryStatus.Echouee, DeliveryStatus.Annulee],
        [DeliveryStatus.EnCours] = [DeliveryStatus.Livree, DeliveryStatus.Echouee],
        [DeliveryStatus.Echouee] = [DeliveryStatus.Affectee, DeliveryStatus.EnCours, DeliveryStatus.Annulee],
        [DeliveryStatus.Livree] = [],
        [DeliveryStatus.Annulee] = []
    };

    /// <summary>Etats finaux.</summary>
    public static readonly DeliveryStatus[] FinalStates = [DeliveryStatus.Livree, DeliveryStatus.Annulee];

    /// <summary>Indique si la transition est autorisee.</summary>
    public static bool CanTransition(DeliveryStatus from, DeliveryStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Etats accessibles depuis un etat donne.</summary>
    public static IReadOnlyCollection<DeliveryStatus> NextStates(DeliveryStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Indique si l'etat est final.</summary>
    public static bool IsFinal(DeliveryStatus status) => FinalStates.Contains(status);

    /// <summary>Indique si une affectation de livreur est encore possible.</summary>
    public static bool AllowsAssignment(DeliveryStatus status) =>
        status is DeliveryStatus.EnAttente or DeliveryStatus.Affectee or DeliveryStatus.Echouee;

    /// <summary>Verifie une transition, ou leve un 409 explicite.</summary>
    public static void EnsureCanTransition(DeliveryStatus from, DeliveryStatus to, string reference)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        var next = NextStates(from);

        var detail = IsFinal(from)
            ? $"La livraison {reference} est dans l'etat final « {from} » : aucune transition n'est plus possible " +
              $"(transition demandee : « {to} »)."
            : $"La livraison {reference} est dans l'etat « {from} » : la transition vers « {to} » n'est pas " +
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
