using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.NotificationService.Data;

/// <summary>
/// Migre la base de notification-service. Aucun seeding : les notifications
/// naissent des evenements reellement emis par les autres services, ce qui rend
/// la demonstration honnete (rien n'est pre-rempli).
/// </summary>
public static class NotificationDbInitializer
{
    /// <summary>Applique les migrations EF Core.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<NotificationDbContext>>();

        logger.LogInformation("Application des migrations EF Core (notification-service)...");
        await context.Database.MigrateAsync(cancellationToken);

        var pending = await context.Notifications.CountAsync(cancellationToken);
        logger.LogInformation("{Count} notification(s) deja presente(s) en base.", pending);
    }
}
