using GestionCommandes.Shared.Security;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace GestionCommandes.Gateway.Configuration;

/// <summary>
/// Table de routage de la gateway, ecrite en code plutot qu'en configuration.
///
/// Les adresses viennent des memes variables d'environnement que celles utilisees
/// par les services entre eux (<c>SERVICES__CATALOG</c>, <c>SERVICES__ORDER</c>...).
/// Il n'y a donc qu'un seul endroit a renseigner dans le fichier <c>.env</c>.
/// </summary>
public static class GatewayRoutes
{
    /// <summary>Un cluster par microservice.</summary>
    /// <param name="endpoints">Adresses des services.</param>
    public static IReadOnlyList<ClusterConfig> BuildClusters(ServiceEndpointsOptions endpoints) =>
    [
        Cluster("identity", endpoints.Identity),
        Cluster("catalog", endpoints.Catalog),
        Cluster("order", endpoints.Order),
        Cluster("delivery", endpoints.Delivery)
    ];

    /// <summary>
    /// Routes, de la plus prioritaire a la moins prioritaire (plus <c>Order</c>
    /// est bas, plus la route est prioritaire). Les routes anonymes precises
    /// passent avant les routes d'API authentifiees.
    /// </summary>
    public static IReadOnlyList<RouteConfig> BuildRoutes() =>
    [
        // --- Accessibles sans jeton ------------------------------------------
        Anonymous("auth-login", "identity", "/api/auth/login", order: 0),
        Anonymous("auth-roles", "identity", "/api/auth/roles", order: 1),
        Anonymous("order-workflow", "order", "/api/orders/workflow", order: 2),
        Anonymous("delivery-workflow", "delivery", "/api/deliveries/workflow", order: 3),

        // --- Agregation OpenAPI pour l'interface Swagger unique --------------
        OpenApi("openapi-identity", "identity", "/openapi/identity/v1.json"),
        OpenApi("openapi-catalog", "catalog", "/openapi/catalog/v1.json"),
        OpenApi("openapi-order", "order", "/openapi/order/v1.json"),
        OpenApi("openapi-delivery", "delivery", "/openapi/delivery/v1.json"),

        // --- API : jeton exige des la frontiere ------------------------------
        Api("identity-auth", "identity", "/api/auth/{**reste}"),
        Api("identity-users", "identity", "/api/users/{**reste}"),

        Api("catalog-categories", "catalog", "/api/categories/{**reste}"),
        Api("catalog-products", "catalog", "/api/products/{**reste}"),
        Api("catalog-stock", "catalog", "/api/stock/{**reste}"),

        Api("order-customers", "order", "/api/customers/{**reste}"),
        Api("order-orders", "order", "/api/orders/{**reste}"),

        Api("delivery-couriers", "delivery", "/api/couriers/{**reste}"),
        Api("delivery-deliveries", "delivery", "/api/deliveries/{**reste}"),
        Api("delivery-notifications", "delivery", "/api/notifications/{**reste}")
    ];

    /// <summary>
    /// Transformation appliquee a TOUTES les routes.
    ///
    /// Point de securite essentiel : l'en-tete <c>X-Internal-Key</c> est SUPPRIME
    /// de chaque requete entrante. Sans cela, un client exterieur pourrait
    /// presenter cette cle et obtenir le role technique « Service » aupres des
    /// microservices. Cette cle n'a de sens qu'entre conteneurs.
    /// </summary>
    /// <param name="context">Contexte de construction des transformations YARP.</param>
    public static void ApplyGlobalTransforms(TransformBuilderContext context)
    {
        context.RequestTransforms.Add(new RequestHeaderRemoveTransform(InternalKey.HeaderName));

        // En-tetes de proxy standards, utiles dans les journaux des services.
        context.AddXForwarded();
    }

    private static ClusterConfig Cluster(string id, string address) => new()
    {
        ClusterId = id,
        Destinations = new Dictionary<string, DestinationConfig>
        {
            [$"{id}-1"] = new() { Address = address.TrimEnd('/') + "/" }
        },
        // Sonde active : YARP ecarte une destination qui ne repond plus.
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

    private static RouteConfig Api(string routeId, string clusterId, string path) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = 10,
        AuthorizationPolicy = "default",
        Match = new RouteMatch { Path = path }
    };

    private static RouteConfig Anonymous(string routeId, string clusterId, string path, int order) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = order,
        AuthorizationPolicy = "anonymous",
        Match = new RouteMatch { Path = path }
    };

    /// <summary>Route d'agregation OpenAPI : reecrit le chemin vers le document du service.</summary>
    private static RouteConfig OpenApi(string routeId, string clusterId, string path) => new()
    {
        RouteId = routeId,
        ClusterId = clusterId,
        Order = 5,
        AuthorizationPolicy = "anonymous",
        Match = new RouteMatch { Path = path },
        Transforms = [new Dictionary<string, string> { ["PathSet"] = "/swagger/v1/swagger.json" }]
    };
}
