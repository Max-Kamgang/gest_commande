using GestionCommandes.BuildingBlocks.Http;
using GestionCommandes.BuildingBlocks.Security;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>
/// Table de routage de la gateway, construite <b>en code</b> a partir de
/// <see cref="ServiceEndpointsOptions"/>.
///
/// Ce choix est volontaire : les adresses des services viennent des memes
/// variables d'environnement que celles utilisees par les services entre eux
/// (<c>SERVICES__CATALOG</c>, <c>SERVICES__INVENTORY</c>...). Il n'y a donc
/// qu'un seul endroit a renseigner dans le fichier <c>.env</c>, au lieu de
/// dupliquer les destinations dans une section <c>ReverseProxy</c>.
/// </summary>
public static class GatewayRoutes
{
    /// <summary>Nom de la politique CORS appliquee aux routes d'API.</summary>
    public const string CorsPolicy = "web-client";

    /// <summary>Politique de limitation de debit generale.</summary>
    public const string GeneralRateLimit = "general";

    /// <summary>Politique de limitation de debit renforcee (connexion).</summary>
    public const string LoginRateLimit = "login";

    /// <summary>Construit les clusters YARP (un par microservice).</summary>
    public static IReadOnlyList<ClusterConfig> BuildClusters(ServiceEndpointsOptions endpoints, string webClientUrl) =>
    [
        Cluster("identity", endpoints.Identity),
        Cluster("catalog", endpoints.Catalog),
        Cluster("inventory", endpoints.Inventory),
        Cluster("order", endpoints.Order),
        Cluster("delivery", endpoints.Delivery),
        Cluster("notification", endpoints.Notification),
        Cluster("web-client", webClientUrl)
    ];

    /// <summary>
    /// Construit les routes. L'ordre est explicite : plus la valeur de
    /// <c>Order</c> est basse, plus la route est prioritaire. Les routes
    /// anonymes precises passent donc avant les routes d'API authentifiees,
    /// elles-memes avant la route de repli vers le client web.
    /// </summary>
    public static IReadOnlyList<RouteConfig> BuildRoutes() =>
    [
        // ---------------- Routes anonymes precises (ordre 0 a 9) -------------
        Anonymous("auth-login", "identity", "/api/auth/login", LoginRateLimit, order: 0),
        Anonymous("auth-roles", "identity", "/api/auth/roles", order: 1),
        Anonymous("order-workflow", "order", "/api/orders/workflow", order: 2),
        Anonymous("delivery-workflow", "delivery", "/api/deliveries/workflow", order: 3),
        Anonymous("event-types", "notification", "/api/events/types", order: 4),

        // ---------- Agregation OpenAPI : /openapi/<service>/v1.json ---------
        // Permet a l'interface Swagger de la gateway d'afficher les six API,
        // alors qu'aucun service n'est publie sur l'hote.
        OpenApi("openapi-identity", "identity", "/openapi/identity/v1.json", order: 5),
        OpenApi("openapi-catalog", "catalog", "/openapi/catalog/v1.json", order: 5),
        OpenApi("openapi-inventory", "inventory", "/openapi/inventory/v1.json", order: 5),
        OpenApi("openapi-order", "order", "/openapi/order/v1.json", order: 5),
        OpenApi("openapi-delivery", "delivery", "/openapi/delivery/v1.json", order: 5),
        OpenApi("openapi-notification", "notification", "/openapi/notification/v1.json", order: 5),

        // --------------- Routes d'API authentifiees (ordre 10) --------------
        Api("identity-users", "identity", "/api/users/{**reste}"),
        Api("identity-auth", "identity", "/api/auth/{**reste}"),

        Api("catalog-categories", "catalog", "/api/categories/{**reste}"),
        Api("catalog-products", "catalog", "/api/products/{**reste}"),

        Api("inventory-warehouses", "inventory", "/api/warehouses/{**reste}"),
        Api("inventory-stock", "inventory", "/api/stock/{**reste}"),
        Api("inventory-reservations", "inventory", "/api/reservations/{**reste}"),

        Api("order-customers", "order", "/api/customers/{**reste}"),
        Api("order-orders", "order", "/api/orders/{**reste}"),

        Api("delivery-couriers", "delivery", "/api/couriers/{**reste}"),
        Api("delivery-deliveries", "delivery", "/api/deliveries/{**reste}"),

        Api("notification-notifications", "notification", "/api/notifications/{**reste}"),
        Api("notification-events", "notification", "/api/events/{**reste}"),

        // ----------- Repli : tout le reste va au client web (ordre 100) -----
        new RouteConfig
        {
            RouteId = "web-client",
            ClusterId = "web-client",
            Order = 100,
            Match = new RouteMatch { Path = "/{**reste}" }
        }
    ];

    private static ClusterConfig Cluster(string id, string address) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            [$"{id}-1"] = new() { Address = address.TrimEnd('/') + "/" }
        },
        // Sonde passive : YARP ecarte une destination qui repond mal.
        HealthCheck = new HealthCheckConfig
        {
            Active = new ActiveHealthCheckConfig
            {
                Enabled = true,
                Interval = TimeSpan.FromSeconds(20),
                Timeout = TimeSpan.FromSeconds(5),
                Policy = "ConsecutiveFailures",
                Path = "/health/ready"
            }
        },
        Metadata = new Dictionary<string, string>
        {
            ["ConsecutiveFailuresHealthPolicy.Threshold"] = "3"
        }
    };

    /// <summary>Route d'API : authentification exigee des la gateway.</summary>
    private static RouteConfig Api(string routeId, string clusterId, string path) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = 10,
        AuthorizationPolicy = "default",
        CorsPolicy = CorsPolicy,
        RateLimiterPolicy = GeneralRateLimit,
        Match = new RouteMatch { Path = path }
    };

    /// <summary>Route accessible sans jeton.</summary>
    private static RouteConfig Anonymous(
        string routeId,
        string clusterId,
        string path,
        string rateLimiterPolicy = GeneralRateLimit,
        int order = 0) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = order,
        AuthorizationPolicy = "anonymous",
        CorsPolicy = CorsPolicy,
        RateLimiterPolicy = rateLimiterPolicy,
        Match = new RouteMatch { Path = path }
    };

    /// <summary>Route d'agregation OpenAPI : reecrit le chemin vers /swagger/v1/swagger.json.</summary>
    private static RouteConfig OpenApi(string routeId, string clusterId, string path, int order) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = order,
        AuthorizationPolicy = "anonymous",
        CorsPolicy = CorsPolicy,
        RateLimiterPolicy = GeneralRateLimit,
        Match = new RouteMatch { Path = path },
        Transforms =
        [
            new Dictionary<string, string> { ["PathSet"] = "/swagger/v1/swagger.json" }
        ]
    };

    /// <summary>
    /// Transformations appliquees a TOUTES les routes.
    ///
    /// Point de securite essentiel : l'en-tete <c>X-Internal-Key</c> est
    /// <b>supprime</b> de toute requete entrante. Sans cela, un client exterieur
    /// pourrait presenter cette cle et obtenir le role technique <c>Service</c>
    /// aupres des microservices. La cle n'existe que pour les appels emis par un
    /// service, a l'interieur du reseau Docker.
    /// </summary>
    public static void ApplyGlobalTransforms(TransformBuilderContext context)
    {
        context.RequestTransforms.Add(new RequestHeaderRemoveTransform(InternalKeyDefaults.HeaderName));

        // En-tetes standards de proxy, utiles aux logs des services.
        context.AddXForwarded();
        context.AddOriginalHost(false);
    }
}
