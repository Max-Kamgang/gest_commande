namespace GestionCommandes.BuildingBlocks.Security;

/// <summary>Roles applicatifs de la plateforme. Portes par le claim <c>role</c> du JWT.</summary>
public static class Roles
{
    /// <summary>Administrateur technique : acces total, gestion des comptes.</summary>
    public const string Admin = "Admin";

    /// <summary>Gestionnaire : catalogue, stock, commandes, affectation des livraisons.</summary>
    public const string Gestionnaire = "Gestionnaire";

    /// <summary>Livreur : consultation et mise a jour de SES livraisons uniquement.</summary>
    public const string Livreur = "Livreur";

    /// <summary>Client : consultation du catalogue et de SES commandes.</summary>
    public const string Client = "Client";

    /// <summary>Tous les roles connus.</summary>
    public static readonly string[] All = [Admin, Gestionnaire, Livreur, Client];

    /// <summary>Raccourci pour les politiques "back-office".</summary>
    public const string AdminOuGestionnaire = Admin + "," + Gestionnaire;
}

/// <summary>Noms des claims personnalises emis par identity-service.</summary>
public static class AppClaims
{
    /// <summary>Identifiant du livreur associe au compte (pour delivery-service).</summary>
    public const string CourierId = "courier_id";

    /// <summary>Identifiant du client associe au compte (pour order-service).</summary>
    public const string CustomerId = "customer_id";

    /// <summary>Nom complet affichable.</summary>
    public const string FullName = "full_name";
}
