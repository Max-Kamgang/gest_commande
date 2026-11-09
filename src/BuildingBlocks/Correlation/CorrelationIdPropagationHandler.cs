namespace GestionCommandes.BuildingBlocks.Correlation;

/// <summary>
/// <see cref="DelegatingHandler"/> branche sur tous les HttpClient typés :
/// recopie le Correlation-ID de la requete entrante vers l'appel sortant,
/// afin que la trace suive la chaine gateway -> order -> inventory -> notification.
/// </summary>
public sealed class CorrelationIdPropagationHandler(ICorrelationIdAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(CorrelationConstants.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationConstants.HeaderName, accessor.CorrelationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
