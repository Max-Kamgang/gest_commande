using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GestionCommandes.BuildingBlocks.Errors;
using Microsoft.AspNetCore.Mvc;

namespace GestionCommandes.BuildingBlocks.Http;

/// <summary>
/// Traduction uniforme des reponses d'un service appele :
/// <list type="bullet">
///   <item>2xx -&gt; desérialisation du corps,</item>
///   <item>4xx -&gt; <see cref="DownstreamRejectedException"/> (erreur metier relayee),</item>
///   <item>5xx / transport / timeout -&gt; <see cref="ServiceUnavailableException"/> (503 propre, jamais un 500 brut).</item>
/// </list>
/// </summary>
public static class DownstreamHttpExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Envoie la requete et traduit les erreurs de transport en 503.</summary>
    public static async Task<HttpResponseMessage> SendSafeAsync(
        this HttpClient client,
        HttpRequestMessage request,
        string serviceName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout de l'attempt ou du total request (pipeline de resilience).
            throw new ServiceUnavailableException(
                serviceName,
                $"Le service '{serviceName}' n'a pas repondu dans le delai imparti.",
                ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ServiceUnavailableException(
                serviceName,
                $"Le service '{serviceName}' est injoignable ({ex.HttpRequestError}). Verifiez que le conteneur est demarre.",
                ex);
        }
        catch (Exception ex) when (ex.GetType().FullName?.StartsWith("Polly.", StringComparison.Ordinal) == true)
        {
            // Circuit-breaker ouvert, rejet par le rate limiter interne, etc.
            throw new ServiceUnavailableException(
                serviceName,
                $"Le service '{serviceName}' est momentanement indisponible (coupe-circuit ouvert).",
                ex);
        }
    }

    /// <summary>Lit un corps JSON 2xx, ou leve l'exception appropriee.</summary>
    public static async Task<T> ReadContentOrThrowAsync<T>(
        this HttpResponseMessage response,
        string serviceName,
        CancellationToken cancellationToken)
    {
        await response.EnsureDownstreamSuccessAsync(serviceName, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);

        return payload ?? throw new ServiceUnavailableException(
            serviceName,
            $"Le service '{serviceName}' a renvoye un corps vide alors qu'un resultat etait attendu.");
    }

    /// <summary>Verifie le succes, ou leve 503 (5xx) / l'erreur metier relayee (4xx).</summary>
    public static async Task EnsureDownstreamSuccessAsync(
        this HttpResponseMessage response,
        string serviceName,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var (title, detail, errorCode, extensions) = await ReadProblemAsync(response, cancellationToken);

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout)
        {
            throw new ServiceUnavailableException(
                serviceName,
                $"Le service '{serviceName}' a repondu une erreur {(int)response.StatusCode} : {detail}");
        }

        throw new DownstreamRejectedException(
            serviceName,
            response.StatusCode,
            title ?? "Appel refuse par un service dependant",
            detail,
            errorCode,
            extensions);
    }

    private static async Task<(string? Title, string Detail, string? ErrorCode, IDictionary<string, object?>? Extensions)>
        ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var media = response.Content.Headers.ContentType?.MediaType;

            if (media is "application/problem+json" or "application/json")
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions, cancellationToken);

                if (problem is not null)
                {
                    var code = problem.Extensions.TryGetValue("errorCode", out var raw)
                        ? raw?.ToString()
                        : null;

                    return (problem.Title,
                        problem.Detail ?? problem.Title ?? $"HTTP {(int)response.StatusCode}",
                        code,
                        problem.Extensions);
                }
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (null,
                string.IsNullOrWhiteSpace(text) ? $"HTTP {(int)response.StatusCode}" : Truncate(text, 500),
                null,
                null);
        }
        catch (Exception)
        {
            return (null, $"HTTP {(int)response.StatusCode}", null, null);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}
