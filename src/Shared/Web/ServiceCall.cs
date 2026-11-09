using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionCommandes.Shared.Errors;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.Shared.Web;

/// <summary>
/// Appel HTTP d'un service vers un autre.
///
/// Tout passe par ici pour une raison simple : un appel entre conteneurs peut
/// echouer (conteneur pas encore demarre, reseau Docker, delai depasse) et il ne
/// faut jamais que cela devienne un 500 opaque. Cette classe traduit :
/// <list type="bullet">
///   <item>reseau injoignable ou delai depasse -&gt; 503 nomme le service en cause,</item>
///   <item>erreur metier du service appele (400, 404, 409) -&gt; la meme erreur chez l'appelant,</item>
///   <item>succes -&gt; la reponse deserialisee.</item>
/// </list>
/// </summary>
public static class ServiceCall
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Envoie une requete et renvoie la reponse deserialisee.</summary>
    /// <typeparam name="TResponse">Type attendu en reponse.</typeparam>
    /// <param name="client">Client HTTP configure sur le service cible.</param>
    /// <param name="serviceName">Nom du service appele, pour les messages d'erreur.</param>
    /// <param name="method">Methode HTTP.</param>
    /// <param name="path">Chemin relatif, par exemple <c>api/stock/reservations</c>.</param>
    /// <param name="body">Corps a envoyer en JSON, ou <c>null</c>.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public static async Task<TResponse> SendAsync<TResponse>(
        this HttpClient client,
        string serviceName,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(client, serviceName, method, path, body, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, cancellationToken);

        return payload ?? throw new ServiceUnavailableException(
            serviceName,
            $"Reponse vide de {serviceName} sur {method} {path}.");
    }

    /// <summary>Envoie une requete dont la reponse n'est pas exploitee.</summary>
    /// <param name="client">Client HTTP configure sur le service cible.</param>
    /// <param name="serviceName">Nom du service appele.</param>
    /// <param name="method">Methode HTTP.</param>
    /// <param name="path">Chemin relatif.</param>
    /// <param name="body">Corps a envoyer en JSON, ou <c>null</c>.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    public static async Task SendAsync(
        this HttpClient client,
        string serviceName,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
        => (await SendRawAsync(client, serviceName, method, path, body, cancellationToken)).Dispose();

    private static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        string serviceName,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            throw new ServiceUnavailableException(
                serviceName,
                $"{serviceName} est injoignable ({ex.Message}). Verifiez que son conteneur est demarre.");
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // Le service appele a refuse : on reprend son message pour l'appelant.
        var status = response.StatusCode;
        var detail = await ReadProblemDetailAsync(response, cancellationToken);

        response.Dispose();

        // Le code HTTP du service appele devient la meme erreur chez l'appelant :
        // un stock insuffisant reste un 409 pour le client, pas un 500.
        AppException erreur = status switch
        {
            HttpStatusCode.BadRequest => new BadRequestException($"{serviceName} : {detail}"),
            HttpStatusCode.NotFound => new BadRequestException($"{serviceName} : {detail}"),
            HttpStatusCode.Conflict => new ConflictException($"{serviceName} : {detail}"),
            _ => new ServiceUnavailableException(serviceName, $"{serviceName} a repondu {(int)status} : {detail}")
        };

        throw erreur;
    }

    private static async Task<string> ReadProblemDetailAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, cancellationToken);

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title;
            }
        }
        catch (Exception)
        {
            // Reponse non JSON : on se rabat sur le code HTTP.
        }

        return $"reponse HTTP {(int)response.StatusCode}";
    }
}
