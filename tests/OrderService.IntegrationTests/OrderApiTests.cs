using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GestionCommandes.IntegrationTests;

/// <summary>
/// Pipeline HTTP complet d'order-service : autorisation par role, perimetre client,
/// idempotence, machine a etats et degradation propre quand un service est absent.
/// </summary>
public sealed class OrderApiTests(OrderFixture fixture) : IClassFixture<OrderFixture>
{
    private static readonly Guid ClientKamgang = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid ClientLycee = Guid.Parse("33333333-0000-0000-0000-000000000002");
    private static readonly Guid Ordinateur = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Gestionnaire = Guid.Parse("55555555-0000-0000-0000-000000000002");
    private static readonly Guid UtilisateurClient = Guid.Parse("55555555-0000-0000-0000-000000000005");

    private static HttpClient Avec(WebApplicationFactoryHolder fabrique, string role, Guid utilisateur, Guid? client = null)
    {
        var http = fabrique.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Jetons.Creer(role, utilisateur, client));
        return http;
    }

    private static HttpClient Gestion(OrderFixture f) => Avec(new WebApplicationFactoryHolder(f.Factory), "Gestionnaire", Gestionnaire);

    private static HttpClient ClientKam(OrderFixture f) =>
        Avec(new WebApplicationFactoryHolder(f.Factory), "Client", UtilisateurClient, ClientKamgang);

    [Fact]
    public async Task Le_workflow_expose_les_six_etats_et_les_transitions()
    {
        var reponse = await fixture.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/orders/workflow");

        Assert.Equal(6, reponse.GetProperty("states").GetArrayLength());
    }

    [Fact]
    public async Task Un_client_cree_une_commande_pour_son_propre_compte_uniquement()
    {
        var reponse = await ClientKam(fixture).PostAsJsonAsync("/api/orders", new
        {
            customerId = ClientLycee,
            lines = new[] { new { productId = Ordinateur, quantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task La_creation_est_idempotente_sur_lIdempotencyKey()
    {
        var http = Gestion(fixture);
        var requete = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { customerId = ClientKamgang, lines = new[] { new { productId = Ordinateur, quantity = 1 } } })
        };
        requete.Headers.Add("Idempotency-Key", "IT-IDEM-" + Guid.NewGuid());
        var cle = requete.Headers.GetValues("Idempotency-Key").Single();

        var premiere = await http.SendAsync(requete);
        var rejeu = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new { customerId = ClientKamgang, lines = Array.Empty<object>() })
        };
        rejeu.Headers.Add("Idempotency-Key", cle);
        var seconde = await http.SendAsync(rejeu);

        Assert.Equal(HttpStatusCode.Created, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, seconde.StatusCode);
        Assert.True(seconde.Headers.Contains("Idempotent-Replay"));

        var numero1 = (await premiere.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString();
        var numero2 = (await seconde.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("number").GetString();
        Assert.Equal(numero1, numero2);
    }

    [Fact]
    public async Task Expedier_une_commande_en_brouillon_renvoie_409_avec_les_transitions_possibles()
    {
        var http = Gestion(fixture);
        var creation = await http.PostAsJsonAsync("/api/orders", new
        {
            customerId = ClientLycee,
            lines = new[] { new { productId = Ordinateur, quantity = 1 } }
        });
        var id = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var reponse = await http.PostAsync($"/api/orders/{id}/ship", null);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INVALID_STATE_TRANSITION", probleme.GetProperty("errorCode").GetString());
        Assert.Contains("Confirmee", probleme.GetProperty("allowedTransitions").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Une_commande_non_validable_renvoie_400_en_problem_details()
    {
        var reponse = await Gestion(fixture).PostAsJsonAsync("/api/customers", new
        {
            code = "x",
            name = "",
            email = "pas-un-email",
            city = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Une_confirmation_sans_catalogue_renvoie_503_et_la_commande_reste_en_brouillon()
    {
        // Catalogue volontairement injoignable : aucun 500 brut, compensation et motif enregistre.
        using var fabrique = new ServiceFactory(new Dictionary<string, string?>
        {
            ["Services:Catalog"] = "http://127.0.0.1:9"
        });
        var http = Avec(new WebApplicationFactoryHolder(fabrique), "Gestionnaire", Gestionnaire);

        var creation = await http.PostAsJsonAsync("/api/orders", new
        {
            customerId = ClientKamgang,
            lines = new[] { new { productId = Ordinateur, quantity = 1 } }
        });
        var id = (await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var reponse = await http.PostAsync($"/api/orders/{id}/confirm", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reponse.StatusCode);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SERVICE_UNAVAILABLE", probleme.GetProperty("errorCode").GetString());
        Assert.Equal("catalog-service", probleme.GetProperty("dependency").GetString());

        var commande = await http.GetFromJsonAsync<JsonElement>($"/api/orders/{id}");
        Assert.Equal("Brouillon", commande.GetProperty("status").GetString());
        Assert.False(commande.GetProperty("lastError").ValueKind == JsonValueKind.Null);
    }
}

/// <summary>Une seule instance du service pour toute la classe de tests.</summary>
public sealed class OrderFixture : IDisposable
{
    public ServiceFactory Factory { get; } = new();

    public void Dispose() => Factory.Dispose();
}

/// <summary>Simple enveloppe, pour partager une fabrique entre les helpers de test.</summary>
public sealed class WebApplicationFactoryHolder(ServiceFactory factory)
{
    public ServiceFactory Factory { get; } = factory;
}
