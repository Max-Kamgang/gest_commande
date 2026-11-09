namespace GestionCommandes.Shared.Security;

/// <summary>Roles de la plateforme, portes par le claim <c>role</c> du jeton JWT.</summary>
public static class Roles
{
    /// <summary>Administrateur : acces total, gestion des comptes.</summary>
    public const string Admin = "Admin";

    /// <summary>Gestionnaire : catalogue, stock, commandes, affectation des livraisons.</summary>
    public const string Gestionnaire = "Gestionnaire";

    /// <summary>Livreur : consultation et mise a jour de SES livraisons.</summary>
    public const string Livreur = "Livreur";

    /// <summary>Client : consultation du catalogue et de SES commandes.</summary>
    public const string Client = "Client";

    /// <summary>
    /// Role technique obtenu uniquement par un appel de service a service
    /// (en-tete <c>X-Internal-Key</c>). Jamais attribuable a un compte humain.
    /// </summary>
    public const string Service = "Service";

    /// <summary>Roles attribuables a un compte utilisateur.</summary>
    public static readonly string[] Attribuables = [Admin, Gestionnaire, Livreur, Client];
}

/// <summary>Noms des politiques d'autorisation.</summary>
public static class Policies
{
    /// <summary>Admin, Gestionnaire, ou appel interne d'un autre service.</summary>
    public const string BackOffice = "back-office";

    /// <summary>Admin, Gestionnaire, Livreur, ou appel interne d'un autre service.</summary>
    public const string Livraisons = "livraisons";
}

/// <summary>Claims personnalises emis par identity-service.</summary>
public static class AppClaims
{
    /// <summary>Identifiant du livreur lie au compte (role Livreur).</summary>
    public const string CourierId = "courier_id";

    /// <summary>Identifiant du client lie au compte (role Client).</summary>
    public const string CustomerId = "customer_id";

    /// <summary>Nom complet affichable.</summary>
    public const string FullName = "full_name";
}
