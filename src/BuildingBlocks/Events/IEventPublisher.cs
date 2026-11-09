namespace GestionCommandes.BuildingBlocks.Events;

/// <summary>
/// Publication d'evenements vers notification-service.
/// Volontairement <b>fire-and-forget</b> : la methode depose l'evenement dans une file
/// en memoire et rend la main immediatement. L'envoi HTTP est fait par un service de
/// fond ; un echec est journalise mais ne remonte jamais dans le flux metier principal.
/// </summary>
public interface IEventPublisher
{
    /// <summary>Met un evenement en file d'attente pour diffusion.</summary>
    /// <returns><c>true</c> si l'evenement a ete accepte dans la file.</returns>
    bool Publish(IntegrationEvent integrationEvent);
}
