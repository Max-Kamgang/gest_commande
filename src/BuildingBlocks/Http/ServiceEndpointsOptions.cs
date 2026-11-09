namespace GestionCommandes.BuildingBlocks.Http;

/// <summary>
/// Adresses des microservices, resolues par nom DNS Docker en conteneur
/// (<c>http://inventory-service:8080</c>) et par <c>localhost:50xx</c> en local.
/// Surchargeable par variables d'environnement : <c>SERVICES__INVENTORY</c>, etc.
/// </summary>
public sealed class ServiceEndpointsOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Services";

    public string Identity { get; set; } = "http://localhost:5001";
    public string Catalog { get; set; } = "http://localhost:5002";
    public string Inventory { get; set; } = "http://localhost:5003";
    public string Order { get; set; } = "http://localhost:5004";
    public string Delivery { get; set; } = "http://localhost:5005";
    public string Notification { get; set; } = "http://localhost:5006";
}
