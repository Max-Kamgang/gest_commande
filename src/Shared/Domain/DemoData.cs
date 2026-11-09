namespace GestionCommandes.Shared.Domain;

/// <summary>
/// Identifiants du jeu de donnees de demonstration, partages par les quatre services.
///
/// Chaque service a sa propre base : il n'existe donc aucune cle etrangere entre
/// services. La coherence des identifiants (produits, clients, livreurs) est
/// assuree ici, par des GUID fixes et lisibles. C'est ce qui permet au scenario
/// de demonstration de fonctionner de bout en bout des le premier demarrage.
/// </summary>
public static class DemoIds
{
    /// <summary>Comptes de test, un par role (identity-service).</summary>
    public static class Users
    {
        public static readonly Guid Admin = new("55555555-0000-0000-0000-000000000001");
        public static readonly Guid Gestionnaire = new("55555555-0000-0000-0000-000000000002");
        public static readonly Guid Livreur = new("55555555-0000-0000-0000-000000000003");
        public static readonly Guid Client = new("55555555-0000-0000-0000-000000000004");
    }

    /// <summary>Categories du catalogue (catalog-service).</summary>
    public static class Categories
    {
        public static readonly Guid Informatique = new("66666666-0000-0000-0000-000000000001");
        public static readonly Guid Peripheriques = new("66666666-0000-0000-0000-000000000002");
        public static readonly Guid Reseau = new("66666666-0000-0000-0000-000000000003");
        public static readonly Guid Energie = new("66666666-0000-0000-0000-000000000004");
    }

    /// <summary>Produits (catalog-service, references par order-service).</summary>
    public static class Products
    {
        public static readonly Guid OrdinateurPortable = new("22222222-0000-0000-0000-000000000001");
        public static readonly Guid Ecran24 = new("22222222-0000-0000-0000-000000000002");
        public static readonly Guid ClavierMecanique = new("22222222-0000-0000-0000-000000000003");
        public static readonly Guid SourisSansFil = new("22222222-0000-0000-0000-000000000004");
        public static readonly Guid Switch8Ports = new("22222222-0000-0000-0000-000000000005");
        public static readonly Guid CableReseau = new("22222222-0000-0000-0000-000000000006");
        public static readonly Guid Onduleur = new("22222222-0000-0000-0000-000000000007");
        public static readonly Guid DisqueSsd = new("22222222-0000-0000-0000-000000000008");
    }

    /// <summary>Clients (order-service).</summary>
    public static class Customers
    {
        public static readonly Guid EtsKamgang = new("33333333-0000-0000-0000-000000000001");
        public static readonly Guid LyceeDeido = new("33333333-0000-0000-0000-000000000002");
        public static readonly Guid CyberAkwa = new("33333333-0000-0000-0000-000000000003");
    }

    /// <summary>Livreurs (delivery-service).</summary>
    public static class Couriers
    {
        public static readonly Guid PaulMbarga = new("44444444-0000-0000-0000-000000000001");
        public static readonly Guid AlineNjoya = new("44444444-0000-0000-0000-000000000002");
    }
}

/// <summary>Categorie de demonstration.</summary>
/// <param name="Id">Identifiant fixe.</param>
/// <param name="Code">Code court.</param>
/// <param name="Name">Libelle.</param>
public sealed record DemoCategory(Guid Id, string Code, string Name);

/// <summary>
/// Produit de demonstration, avec son stock initial : dans cette version
/// catalog-service gere a la fois la fiche produit et son stock.
/// </summary>
/// <param name="Id">Identifiant fixe.</param>
/// <param name="Sku">Reference article.</param>
/// <param name="Name">Libelle.</param>
/// <param name="CategoryId">Categorie de rattachement.</param>
/// <param name="UnitPrice">Prix unitaire en XAF.</param>
/// <param name="Stock">Quantite physique initiale.</param>
/// <param name="LowStockThreshold">Seuil d'alerte de stock bas.</param>
public sealed record DemoProduct(
    Guid Id,
    string Sku,
    string Name,
    Guid CategoryId,
    decimal UnitPrice,
    int Stock,
    int LowStockThreshold);

/// <summary>Client de demonstration.</summary>
/// <param name="Id">Identifiant fixe.</param>
/// <param name="Code">Code client.</param>
/// <param name="Name">Raison sociale.</param>
/// <param name="Email">Adresse de contact.</param>
/// <param name="Phone">Telephone.</param>
/// <param name="City">Ville.</param>
/// <param name="Address">Adresse de livraison.</param>
public sealed record DemoCustomer(
    Guid Id,
    string Code,
    string Name,
    string Email,
    string Phone,
    string City,
    string Address);

/// <summary>Livreur de demonstration.</summary>
/// <param name="Id">Identifiant fixe.</param>
/// <param name="Code">Code livreur.</param>
/// <param name="FullName">Nom complet.</param>
/// <param name="Email">Adresse de contact.</param>
/// <param name="Phone">Telephone.</param>
/// <param name="Vehicle">Moyen de transport.</param>
/// <param name="Zone">Zone de tournee.</param>
public sealed record DemoCourier(
    Guid Id,
    string Code,
    string FullName,
    string Email,
    string Phone,
    string Vehicle,
    string Zone);

/// <summary>Contenu du jeu de donnees de demonstration.</summary>
public static class DemoData
{
    /// <summary>Devise de la plateforme.</summary>
    public const string Currency = "XAF";

    /// <summary>Taux de TVA applique aux commandes (19,25 %).</summary>
    public const decimal VatRate = 0.1925m;

    /// <summary>Mot de passe des comptes de demonstration.</summary>
    public const string DemoPassword = "Demo@2026";

    /// <summary>Categories du catalogue.</summary>
    public static readonly DemoCategory[] Categories =
    [
        new(DemoIds.Categories.Informatique, "INFO", "Informatique"),
        new(DemoIds.Categories.Peripheriques, "PERIPH", "Peripheriques"),
        new(DemoIds.Categories.Reseau, "RESEAU", "Reseau"),
        new(DemoIds.Categories.Energie, "ENERGIE", "Energie")
    ];

    /// <summary>
    /// Produits et stock initial. Le cable reseau est volontairement juste au-dessus
    /// de son seuil : deux commandes suffisent a declencher une alerte de stock bas
    /// pendant la demonstration.
    /// </summary>
    public static readonly DemoProduct[] Products =
    [
        new(DemoIds.Products.OrdinateurPortable, "INFO-PC-001", "Ordinateur portable 15 pouces i5",
            DemoIds.Categories.Informatique, 450_000m, 25, 5),
        new(DemoIds.Products.Ecran24, "INFO-ECR-024", "Ecran 24 pouces Full HD",
            DemoIds.Categories.Informatique, 95_000m, 40, 10),
        new(DemoIds.Products.ClavierMecanique, "PER-CLA-001", "Clavier mecanique AZERTY",
            DemoIds.Categories.Peripheriques, 28_500m, 60, 15),
        new(DemoIds.Products.SourisSansFil, "PER-SOU-002", "Souris sans fil 1600 dpi",
            DemoIds.Categories.Peripheriques, 9_500m, 85, 20),
        new(DemoIds.Products.Switch8Ports, "RES-SW-008", "Switch 8 ports Gigabit",
            DemoIds.Categories.Reseau, 42_000m, 18, 5),
        new(DemoIds.Products.CableReseau, "RES-CAB-006", "Cable reseau Cat6 (3 m)",
            DemoIds.Categories.Reseau, 3_500m, 22, 20),
        new(DemoIds.Products.Onduleur, "ENE-OND-1000", "Onduleur 1000 VA",
            DemoIds.Categories.Energie, 135_000m, 10, 4),
        new(DemoIds.Products.DisqueSsd, "INFO-SSD-512", "Disque SSD 512 Go",
            DemoIds.Categories.Informatique, 55_000m, 35, 8)
    ];

    /// <summary>Clients.</summary>
    public static readonly DemoCustomer[] Customers =
    [
        new(DemoIds.Customers.EtsKamgang, "CLI-001", "Ets Kamgang et Fils",
            "client@micro.local", "+237690000001", "Douala", "Rue Joss, Bonanjo"),
        new(DemoIds.Customers.LyceeDeido, "CLI-002", "Lycee Bilingue de Deido",
            "intendance@deido.micro.local", "+237690000002", "Douala", "Avenue Deido"),
        new(DemoIds.Customers.CyberAkwa, "CLI-003", "Cyber Akwa Nord",
            "contact@cyberakwa.micro.local", "+237690000003", "Douala", "Carrefour Akwa Nord")
    ];

    /// <summary>Livreurs.</summary>
    public static readonly DemoCourier[] Couriers =
    [
        new(DemoIds.Couriers.PaulMbarga, "LIV-001", "Paul Mbarga",
            "livreur@micro.local", "+237691000001", "Camionnette", "Douala centre"),
        new(DemoIds.Couriers.AlineNjoya, "LIV-002", "Aline Njoya",
            "livreur2@micro.local", "+237691000002", "Moto", "Douala nord")
    ];
}
