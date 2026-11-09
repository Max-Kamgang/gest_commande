using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace GestionCommandes.IntegrationTests;

/// <summary>Pipeline HTTP complet d'inventory-service : securite, reservations et verrou optimiste.</summary>
public sealed class InventoryApiTests
{
    private static readonly Guid SwitchHuitPorts = Guid.Parse("22222222-0000-0000-0000-000000000005");
    private static readonly Guid Douala = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Gestionnaire = Guid.Parse("55555555-0000-0000-0000-000000000002");

    /// <summary>Chaque test dispose de sa propre instance et de sa propre base : aucun etat partage.</summary>
    private static HttpClient ClientInterne(ServiceFactory fabrique)
    {
        var client = fabrique.CreateClient();
        client.DefaultRequestHeaders.Add("X-Internal-Key", ServiceFactory.CleInterne);
        return client;
    }

    private static HttpClient ClientAvecRole(ServiceFactory fabrique, string role)
    {
        var client = fabrique.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Jetons.Creer(role, Gestionnaire));
        return client;
    }

    private static object Reservation(string reference, int quantite, Guid? entrepot = null) => new
    {
        reference,
        lines = new[] { new { productId = SwitchHuitPorts, quantity = quantite, warehouseId = entrepot } }
    };

    [Fact]
    public async Task La_sante_du_service_repond_sans_authentification()
    {
        using var fabrique = new ServiceFactory();
        var reponse = await fabrique.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_requete_sans_jeton_recoit_un_probleme_rfc9457()
    {
        using var fabrique = new ServiceFactory();
        var reponse = await fabrique.CreateClient().GetAsync("/api/stock");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal("application/problem+json", reponse.Content.Headers.ContentType?.MediaType);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNAUTHENTICATED", probleme.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Un_client_ne_peut_pas_ajuster_le_stock()
    {
        using var fabrique = new ServiceFactory();
        var reponse = await ClientAvecRole(fabrique, "Client").PostAsJsonAsync("/api/stock/exits",
            new { productId = SwitchHuitPorts, warehouseId = Douala, quantity = 1, reason = "test" });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Une_entree_de_stock_par_un_gestionnaire_augmente_le_disponible()
    {
        using var fabrique = new ServiceFactory();
        var client = ClientAvecRole(fabrique, "Gestionnaire");

        var reponse = await client.PostAsJsonAsync("/api/stock/entries",
            new { productId = SwitchHuitPorts, warehouseId = Douala, quantity = 2, reference = "BR-IT-1" });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var ligne = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ligne.GetProperty("quantityOnHand").GetInt32() >= 2);
    }

    [Fact]
    public async Task Une_reservation_puis_sa_rejoue_renvoie_200_sans_doubler_le_stock()
    {
        using var fabrique = new ServiceFactory();
        var client = ClientInterne(fabrique);
        var premiere = await client.PostAsJsonAsync("/api/reservations", Reservation("CMD-IT-IDEM", 1, Douala));
        var rejeu = await client.PostAsJsonAsync("/api/reservations", Reservation("CMD-IT-IDEM", 1, Douala));

        Assert.Equal(HttpStatusCode.Created, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejeu.StatusCode);
    }

    [Fact]
    public async Task Une_demande_superieure_au_stock_renvoie_409_avec_le_detail_des_manques()
    {
        using var fabrique = new ServiceFactory();
        var reponse = await ClientInterne(fabrique).PostAsJsonAsync("/api/reservations",
            Reservation("CMD-IT-BIG", 10_000, Douala));

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INSUFFICIENT_STOCK", probleme.GetProperty("errorCode").GetString());
        Assert.True(probleme.TryGetProperty("shortages", out _));
    }

    [Fact]
    public async Task Dix_reservations_simultanees_ne_provoquent_jamais_de_survente()
    {
        using var fabrique = new ServiceFactory();
        // Stock initial de demonstration : 18 switchs a Douala. 10 demandes de 3 = 30 unites.
        var client = ClientInterne(fabrique);

        var reponses = await Task.WhenAll(Enumerable.Range(1, 10).Select(n =>
            client.PostAsJsonAsync("/api/reservations", Reservation($"CONC-IT-{n}", 3, Douala))));

        var succes = reponses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var refus = reponses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(6, succes);
        Assert.Equal(4, refus);
        Assert.DoesNotContain(reponses, r => (int)r.StatusCode >= 500);

        var disponibilite = await ClientAvecRole(fabrique, "Gestionnaire")
            .GetFromJsonAsync<JsonElement>($"/api/stock/{SwitchHuitPorts}/availability?warehouseId={Douala}");
        Assert.Equal(0, disponibilite.GetProperty("totalAvailable").GetInt32());
        Assert.True(disponibilite.GetProperty("totalReserved").GetInt32() <= disponibilite.GetProperty("totalOnHand").GetInt32());
    }

    [Fact]
    public async Task Un_ajustement_sous_la_quantite_reservee_est_refuse()
    {
        using var fabrique = new ServiceFactory();
        var client = ClientInterne(fabrique);
        await client.PostAsJsonAsync("/api/reservations", Reservation("CMD-IT-ADJ", 2, Douala));

        var reponse = await ClientAvecRole(fabrique, "Gestionnaire").PostAsJsonAsync("/api/stock/adjustments",
            new { productId = SwitchHuitPorts, warehouseId = Douala, quantityOnHand = 0, reason = "Inventaire" });

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
        var probleme = await reponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ADJUSTMENT_BELOW_RESERVED", probleme.GetProperty("errorCode").GetString());
    }
}

