using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestionCommandes.Shared.Data;

/// <summary>
/// Classe de base des DbContext. Chaque service a sa PROPRE base SQLite :
/// cette classe ne partage que deux conventions techniques imposees par SQLite.
///
/// 1. Les dates sont stockees en millisecondes Unix UTC, car SQLite ne sait pas
///    trier un <see cref="DateTimeOffset"/> stocke en texte.
/// 2. Les montants sont stockes en centimes (entier) grace a
///    <see cref="MoneyExtensions.AsMoney"/>, car SQLite n'a pas de type decimal
///    et comparerait les prix comme du texte (« 9500 » &gt; « 450000 »).
/// </summary>
public abstract class ServiceDbContext(DbContextOptions options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUnixConverter>();

        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableDateTimeOffsetToUnixConverter>();
    }
}

/// <summary>Conversion date -&gt; millisecondes Unix UTC.</summary>
public sealed class DateTimeOffsetToUnixConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetToUnixConverter()
        : base(
            value => value.ToUniversalTime().ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value))
    {
    }
}

/// <summary>Variante pour les dates facultatives.</summary>
public sealed class NullableDateTimeOffsetToUnixConverter : ValueConverter<DateTimeOffset?, long?>
{
    public NullableDateTimeOffsetToUnixConverter()
        : base(
            value => value.HasValue ? value.Value.ToUniversalTime().ToUnixTimeMilliseconds() : null,
            value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null)
    {
    }
}

/// <summary>Configuration des colonnes monetaires.</summary>
public static class MoneyExtensions
{
    /// <summary>Stocke un montant en centimes : tris, filtres et sommes restent exacts.</summary>
    public static PropertyBuilder<decimal> AsMoney(this PropertyBuilder<decimal> builder) =>
        builder.HasConversion(
            amount => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero),
            minor => minor / 100m);
}
