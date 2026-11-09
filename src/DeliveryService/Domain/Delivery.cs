using System.ComponentModel.DataAnnotations;
using GestionCommandes.Shared.Errors;

namespace GestionCommandes.DeliveryService.Domain;

/// <summary>Etats d'une livraison.</summary>
public enum DeliveryStatus
{
    /// <summary>Bordereau cree, aucun livreur affecte.</summary>
    EnAttente = 1,

    /// <summary>Livreur affecte, enlevement a faire.</summary>
    Affectee = 2,

    /// <summary>Marchandise enlevee, livraison en route.</summary>
    EnCours = 3,

    /// <summary>Remise au client effectuee. Etat final.</summary>
    Livree = 4,

    /// <summary>Tentative infructueuse (client absent, adresse introuvable). Reaffectable.</summary>
    Echouee = 5,

    /// <summary>Livraison annulee. Etat final.</summary>
    Annulee = 6
}

/// <summary>
/// Machine a etats de la livraison. Une tentative echouee reste reaffectable :
/// c'est le cas reel du client absent, on repasse le lendemain.
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

    /// <summary>Indique si une transition est autorisee.</summary>
    /// <param name="from">Etat courant.</param>
    /// <param name="to">Etat demande.</param>
    public static bool CanTransition(DeliveryStatus from, DeliveryStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>Etats accessibles depuis un etat donne.</summary>
    /// <param name="from">Etat courant.</param>
    public static IReadOnlyCollection<DeliveryStatus> NextStates(DeliveryStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Indique si l'etat est final.</summary>
    /// <param name="status">Etat a tester.</param>
    public static bool IsFinal(DeliveryStatus status) => NextStates(status).Count == 0;

    /// <summary>Indique si un livreur peut encore etre affecte.</summary>
    /// <param name="status">Etat a tester.</param>
    public static bool AllowsAssignment(DeliveryStatus status) =>
        status is DeliveryStatus.EnAttente or DeliveryStatus.Affectee or DeliveryStatus.Echouee;

    /// <summary>Verifie une transition, ou leve un 409 explicite.</summary>
    /// <param name="from">Etat courant.</param>
    /// <param name="to">Etat demande.</param>
    /// <param name="reference">Reference du bordereau, pour le message.</param>
    public static void EnsureCanTransition(DeliveryStatus from, DeliveryStatus to, string reference)
    {
        if (CanTransition(from, to))
        {
            return;
        }

        var next = NextStates(from);

        throw new ConflictException(IsFinal(from)
            ? $"La livraison {reference} est dans l'etat final « {from} » : aucune transition n'est plus " +
              $"possible (transition demandee : « {to} »)."
            : $"La livraison {reference} est dans l'etat « {from} » : la transition vers « {to} » n'est pas " +
              $"autorisee. Transition(s) possible(s) : {string.Join(", ", next)}.");
    }
}

/// <summary>Livreur.</summary>
public class Courier
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Code livreur unique, par exemple <c>LIV-001</c>.</summary>
    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Nom complet.</summary>
    [MaxLength(160)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Adresse email.</summary>
    [MaxLength(160)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Telephone.</summary>
    [MaxLength(30)]
    public string? Phone { get; set; }

    /// <summary>Moyen de transport.</summary>
    [MaxLength(40)]
    public string Vehicle { get; set; } = "Moto";

    /// <summary>Zone de tournee habituelle.</summary>
    [MaxLength(80)]
    public string? Zone { get; set; }

    /// <summary>Un livreur inactif ne recoit plus d'affectation.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Livraisons affectees.</summary>
    public List<Delivery> Deliveries { get; set; } = [];
}

/// <summary>
/// Bordereau de livraison, cree par order-service quand la commande est expediee.
/// Le numero de commande est unique : un second appel renvoie le bordereau
/// existant au lieu d'en creer un doublon.
/// </summary>
public class Delivery
{
    /// <summary>Identifiant.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Reference lisible unique (<c>LIV-2026-00001</c>).</summary>
    [MaxLength(30)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>Commande d'origine (identifiant gere par order-service).</summary>
    public Guid OrderId { get; set; }

    /// <summary>Numero de commande : c'est la cle de non-doublon.</summary>
    [MaxLength(30)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Client.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Nom du client, recopie de la commande.</summary>
    [MaxLength(160)]
    public string CustomerName { get; set; } = string.Empty;

    /// <summary>Telephone du client, pour le livreur.</summary>
    [MaxLength(30)]
    public string? CustomerPhone { get; set; }

    /// <summary>Adresse de livraison.</summary>
    [MaxLength(200)]
    public string? ShippingAddress { get; set; }

    /// <summary>Ville de livraison.</summary>
    [MaxLength(80)]
    public string? ShippingCity { get; set; }

    /// <summary>Etat courant.</summary>
    public DeliveryStatus Status { get; set; } = DeliveryStatus.EnAttente;

    /// <summary>Livreur affecte.</summary>
    public Guid? CourierId { get; set; }

    /// <summary>Navigation vers le livreur.</summary>
    public Courier? Courier { get; set; }

    /// <summary>Personne ayant receptionne la marchandise.</summary>
    [MaxLength(160)]
    public string? ReceivedBy { get; set; }

    /// <summary>Motif du dernier echec.</summary>
    [MaxLength(300)]
    public string? FailureReason { get; set; }

    /// <summary>Nombre de tentatives infructueuses.</summary>
    public int AttemptCount { get; set; }

    /// <summary>Vrai une fois order-service averti de la livraison effective.</summary>
    public bool OrderNotified { get; set; }

    /// <summary>Date de creation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Date d'affectation.</summary>
    public DateTimeOffset? AssignedAt { get; set; }

    /// <summary>Date d'enlevement.</summary>
    public DateTimeOffset? PickedUpAt { get; set; }

    /// <summary>Date de remise au client.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Verrou optimiste.</summary>
    public Guid RowVersion { get; set; } = Guid.NewGuid();

    /// <summary>Contenu du bordereau.</summary>
    public List<DeliveryLine> Lines { get; set; } = [];

    /// <summary>Historique de suivi.</summary>
    public List<DeliveryTrackingEntry> Tracking { get; set; } = [];

    /// <summary>Ajoute une etape au suivi.</summary>
    /// <param name="from">Etat de depart, nul a la creation.</param>
    /// <param name="to">Nouvel etat.</param>
    /// <param name="changedBy">Auteur.</param>
    /// <param name="comment">Commentaire ou lieu.</param>
    public void AppendTracking(DeliveryStatus? from, DeliveryStatus to, string changedBy, string? comment = null)
        => Tracking.Add(new DeliveryTrackingEntry
        {
            DeliveryId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedBy = changedBy,
            Comment = comment
        });
}

/// <summary>Ligne du bordereau.</summary>
public class DeliveryLine
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Bordereau de rattachement.</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>Navigation vers le bordereau.</summary>
    public Delivery? Delivery { get; set; }

    /// <summary>Produit livre.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Reference article.</summary>
    [MaxLength(40)]
    public string? Sku { get; set; }

    /// <summary>Libelle produit.</summary>
    [MaxLength(160)]
    public string? ProductName { get; set; }

    /// <summary>Quantite.</summary>
    public int Quantity { get; set; }
}

/// <summary>Etape de suivi d'une livraison.</summary>
public class DeliveryTrackingEntry
{
    /// <summary>Identifiant sequentiel.</summary>
    public long Id { get; set; }

    /// <summary>Bordereau concerne.</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>Navigation vers le bordereau.</summary>
    public Delivery? Delivery { get; set; }

    /// <summary>Etat de depart.</summary>
    public DeliveryStatus? FromStatus { get; set; }

    /// <summary>Nouvel etat.</summary>
    public DeliveryStatus ToStatus { get; set; }

    /// <summary>Commentaire ou lieu declare par le livreur.</summary>
    [MaxLength(300)]
    public string? Comment { get; set; }

    /// <summary>Auteur.</summary>
    [MaxLength(160)]
    public string ChangedBy { get; set; } = "systeme";

    /// <summary>Horodatage.</summary>
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Compteur de numerotation annuelle des bordereaux.</summary>
public class DeliverySequence
{
    /// <summary>Annee civile, cle de la table.</summary>
    public int Year { get; set; }

    /// <summary>Dernier numero attribue.</summary>
    public int LastValue { get; set; }
}
