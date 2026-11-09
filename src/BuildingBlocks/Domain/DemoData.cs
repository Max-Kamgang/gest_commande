namespace GestionCommandes.BuildingBlocks.Domain;

/// <summary>
/// Jeu de donnees de demonstration <b>partage</b> par tous les microservices.
///
/// Chaque service possede sa propre base SQLite : il n'y a donc aucune cle etrangere
/// physique entre services. La coherence des identifiants (produits, entrepots,
/// clients, livreurs) est assuree ici, par des GUID deterministes et lisibles.
/// C'est ce qui permet au scenario de demonstration de bout en bout de fonctionner.
/// </summary>
public static class DemoIds
{
    /// <summary>Comptes de test (un par role).</summary>
    public static class Users
    {
        public static readonly Guid Admin = new("55555555-0000-0000-0000-000000000001");
        public static readonly Guid Gestionnaire = new("55555555-0000-0000-0000-000000000002");
        public static readonly Guid Livreur = new("55555555-0000-0000-0000-000000000003");
        public static readonly Guid Livreur2 = new("55555555-0000-0000-0000-000000000004");
        public static readonly Guid Client = new("55555555-0000-0000-0000-000000000005");
    }

    /// <summary>Entrepots (inventory-service).</summary>
    public static class Warehouses
    {
        public static readonly Guid Douala = new("11111111-0000-0000-0000-000000000001");
        public static readonly Guid Yaounde = new("11111111-0000-0000-0000-000000000002");
    }

    /// <summary>Produits (catalog-service et inventory-service).</summary>
    public static class Products
    {
        public static readonly Guid OrdinateurPortable = new("22222222-0000-0000-0000-000000000001");
        public static readonly Guid EcranVingtQuatre = new("22222222-0000-0000-0000-000000000002");
        public static readonly Guid ClavierMecanique = new("22222222-0000-0000-0000-000000000003");
        public static readonly Guid SourisSansFil = new("22222222-0000-0000-0000-000000000004");
        public static readonly Guid SwitchHuitPorts = new("22222222-0000-0000-0000-000000000005");
        public static readonly Guid CableReseau = new("22222222-0000-0000-0000-000000000006");
        public static readonly Guid OnduleurMilleVa = new("22222222-0000-0000-0000-000000000007");
        public static readonly Guid DisqueSsd = new("22222222-0000-0000-0000-000000000008");
    }

    /// <summary>Categories (catalog-service).</summary>
    public static class Categories
    {
        public static readonly Guid Informatique = new("66666666-0000-0000-0000-000000000001");
        public static readonly Guid Peripheriques = new("66666666-0000-0000-0000-000000000002");
        public static readonly Guid Reseau = new("66666666-0000-0000-0000-000000000003");
        public static readonly Guid Energie = new("66666666-0000-0000-0000-000000000004");
    }

    /// <summary>Clients (order-service).</summary>
    public static class Customers
    {
        public static readonly Guid EtsKamgang = new("33333333-0000-0000-0000-000000000001");
        public static readonly Guid LyceeBilingue = new("33333333-0000-0000-0000-000000000002");
        public static readonly Guid CyberAkwa = new("33333333-0000-0000-0000-000000000003");
    }

    /// <summary>Livreurs (delivery-service).</summary>
    public static class Couriers
    {
        public static readonly Guid PaulMbarga = new("44444444-0000-0000-0000-000000000001");
        public static readonly Guid AlineNjoya = new("44444444-0000-0000-0000-000000000002");
    }
}

/// <summary>Produit de demonstration.</summary>
public sealed record DemoProduct(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    Guid CategoryId,
    decimal UnitPrice,
    string Unit);

/// <summary>Categorie de demonstration.</summary>
public sealed record DemoCategory(Guid Id, string Code, string Name, string Description);

/// <summary>Entrepot de demonstration.</summary>
public sealed record DemoWarehouse(Guid Id, string Code, string Name, string City, string Address);

/// <summary>Niveau de stock initial.</summary>
public sealed record DemoStock(Guid ProductId, Guid WarehouseId, int Quantity, int LowStockThreshold);

/// <summary>Client de demonstration.</summary>
public sealed record DemoCustomer(
    Guid Id,
    string Code,
    string Name,
    string Email,
    string Phone,
    string City,
    string Address);

/// <summary>Livreur de demonstration.</summary>
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
    /// <summary>Devise utilisee par toute la plateforme.</summary>
    public const string Currency = "XAF";

    /// <summary>Taux de TVA applique aux commandes (19,25 % Cameroun).</summary>
    public const decimal VatRate = 0.1925m;

    /// <summary>Categories du catalogue.</summary>
    public static readonly DemoCategory[] Categories =
    [
        new(DemoIds.Categories.Informatique, "INFO", "Informatique", "Unites centrales, portables et ecrans"),
        new(DemoIds.Categories.Peripheriques, "PERIPH", "Peripheriques", "Claviers, souris, accessoires"),
        new(DemoIds.Categories.Reseau, "RESEAU", "Reseau", "Switchs, cables, equipements actifs"),
        new(DemoIds.Categories.Energie, "ENERGIE", "Energie", "Onduleurs et protection electrique")
    ];

    /// <summary>Produits du catalogue (prix en XAF).</summary>
    public static readonly DemoProduct[] Products =
    [
        new(DemoIds.Products.OrdinateurPortable, "INFO-PC-001", "Ordinateur portable 15\" i5",
            "Portable 15 pouces, Core i5, 16 Go RAM, SSD 512 Go", DemoIds.Categories.Informatique, 450_000m, "piece"),
        new(DemoIds.Products.EcranVingtQuatre, "INFO-ECR-024", "Ecran 24 pouces Full HD",
            "Ecran IPS 24 pouces, 1920x1080, HDMI + VGA", DemoIds.Categories.Informatique, 95_000m, "piece"),
        new(DemoIds.Products.ClavierMecanique, "PER-CLA-001", "Clavier mecanique AZERTY",
            "Clavier mecanique filaire, disposition AZERTY", DemoIds.Categories.Peripheriques, 28_500m, "piece"),
        new(DemoIds.Products.SourisSansFil, "PER-SOU-002", "Souris sans fil 1600 dpi",
            "Souris optique sans fil 2,4 GHz, 1600 dpi", DemoIds.Categories.Peripheriques, 9_500m, "piece"),
        new(DemoIds.Products.SwitchHuitPorts, "RES-SW-008", "Switch 8 ports Gigabit",
            "Switch non administrable 8 ports 10/100/1000", DemoIds.Categories.Reseau, 42_000m, "piece"),
        new(DemoIds.Products.CableReseau, "RES-CAB-006", "Cable reseau Cat6 (3 m)",
            "Cordon RJ45 Cat6 blinde, longueur 3 metres", DemoIds.Categories.Reseau, 3_500m, "piece"),
        new(DemoIds.Products.OnduleurMilleVa, "ENE-OND-1000", "Onduleur 1000 VA",
            "Onduleur line-interactive 1000 VA / 600 W", DemoIds.Categories.Energie, 135_000m, "piece"),
        new(DemoIds.Products.DisqueSsd, "INFO-SSD-512", "Disque SSD 512 Go",
            "SSD SATA 2,5 pouces, 512 Go", DemoIds.Categories.Informatique, 55_000m, "piece")
    ];

    /// <summary>Entrepots.</summary>
    public static readonly DemoWarehouse[] Warehouses =
    [
        new(DemoIds.Warehouses.Douala, "DLA-01", "Entrepot principal Douala", "Douala", "Zone industrielle Bassa"),
        new(DemoIds.Warehouses.Yaounde, "YAO-01", "Entrepot relais Yaounde", "Yaounde", "Quartier Mvan")
    ];

    /// <summary>
    /// Stock initial. Le cable reseau est volontairement proche de son seuil
    /// d'alerte afin que la demonstration declenche un evenement « stock bas ».
    /// </summary>
    public static readonly DemoStock[] Stocks =
    [
        new(DemoIds.Products.OrdinateurPortable, DemoIds.Warehouses.Douala, 25, 5),
        new(DemoIds.Products.OrdinateurPortable, DemoIds.Warehouses.Yaounde, 8, 3),
        new(DemoIds.Products.EcranVingtQuatre, DemoIds.Warehouses.Douala, 40, 10),
        new(DemoIds.Products.EcranVingtQuatre, DemoIds.Warehouses.Yaounde, 12, 4),
        new(DemoIds.Products.ClavierMecanique, DemoIds.Warehouses.Douala, 60, 15),
        new(DemoIds.Products.SourisSansFil, DemoIds.Warehouses.Douala, 85, 20),
        new(DemoIds.Products.SwitchHuitPorts, DemoIds.Warehouses.Douala, 18, 5),
        new(DemoIds.Products.SwitchHuitPorts, DemoIds.Warehouses.Yaounde, 6, 3),
        new(DemoIds.Products.CableReseau, DemoIds.Warehouses.Douala, 22, 20),
        new(DemoIds.Products.OnduleurMilleVa, DemoIds.Warehouses.Douala, 10, 4),
        new(DemoIds.Products.DisqueSsd, DemoIds.Warehouses.Douala, 35, 8),
        new(DemoIds.Products.DisqueSsd, DemoIds.Warehouses.Yaounde, 9, 4)
    ];

    /// <summary>Clients.</summary>
    public static readonly DemoCustomer[] Customers =
    [
        new(DemoIds.Customers.EtsKamgang, "CLI-001", "Ets Kamgang & Fils",
            "client@micro.local", "+237690000001", "Douala", "Rue Joss, Bonanjo"),
        new(DemoIds.Customers.LyceeBilingue, "CLI-002", "Lycee Bilingue de Deido",
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

    /// <summary>Retrouve un produit de demonstration par identifiant.</summary>
    public static DemoProduct? FindProduct(Guid productId) =>
        Products.FirstOrDefault(p => p.Id == productId);
}
