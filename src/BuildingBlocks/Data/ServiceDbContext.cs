using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.BuildingBlocks.Data;

/// <summary>
/// Classe de base des DbContext de la plateforme. Chaque microservice possede
/// sa <b>propre</b> base SQLite ; cette classe ne partage que des conventions
/// techniques, jamais des donnees.
///
/// Convention appliquee : les <see cref="DateTimeOffset"/> sont stockes en
/// millisecondes Unix UTC, car SQLite ne peut ni trier ni comparer ce type au
/// format texte.
///
/// Les montants monetaires restent configures explicitement, propriete par
/// propriete, avec <c>HasMoneyConversion()</c> : toutes les proprietes
/// <c>decimal</c> ne sont pas forcement des montants.
/// </summary>
public abstract class ServiceDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableUtcDateTimeOffsetConverter>();
    }
}
