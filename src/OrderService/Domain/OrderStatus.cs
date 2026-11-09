namespace GestionCommandes.OrderService.Domain;

/// <summary>
/// Etats du cycle de vie d'une commande.
///
/// Chemin nominal : <see cref="Brouillon"/> → <see cref="Confirmee"/> →
/// <see cref="EnPreparation"/> → <see cref="Expediee"/> → <see cref="Livree"/>.
/// <see cref="Annulee"/> reste possible tant que la commande n'est pas expediee.
/// </summary>
public enum OrderStatus
{
    /// <summary>Commande en cours de saisie : les lignes sont modifiables, aucun stock n'est engage.</summary>
    Brouillon = 1,

    /// <summary>Prix figes et stock reserve : la commande est engagee.</summary>
    Confirmee = 2,

    /// <summary>Preparation physique en cours dans l'entrepot.</summary>
    EnPreparation = 3,

    /// <summary>Marchandise sortie du stock et livraison creee.</summary>
    Expediee = 4,

    /// <summary>Livraison confirmee par le livreur. Etat final.</summary>
    Livree = 5,

    /// <summary>Commande annulee, stock libere. Etat final.</summary>
    Annulee = 6
}
