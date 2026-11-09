using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestionCommandes.BuildingBlocks.Data;

/// <summary>
/// SQLite ne possede pas de type <c>decimal</c> natif : EF Core le stocke en TEXT,
/// ce qui casse les comparaisons et les tris (« 9500 » &gt; « 450000 » en texte).
/// On stocke donc les montants en <b>unites mineures</b> (entier) : les filtres
/// <c>minPrice</c>/<c>maxPrice</c> et les agregats restent exacts et indexables.
/// </summary>
public sealed class MoneyToMinorUnitsConverter : ValueConverter<decimal, long>
{
    /// <summary>Facteur de conversion (2 decimales conservees).</summary>
    public const decimal Factor = 100m;

    public MoneyToMinorUnitsConverter()
        : base(
            amount => (long)Math.Round(amount * Factor, MidpointRounding.AwayFromZero),
            minorUnits => minorUnits / Factor)
    {
    }
}

/// <summary>Helpers de configuration des colonnes monetaires.</summary>
public static class MoneyConfigurationExtensions
{
    /// <summary>Applique le stockage en unites mineures a une propriete monetaire.</summary>
    public static PropertyBuilder<decimal> HasMoneyConversion(this PropertyBuilder<decimal> builder) =>
        builder.HasConversion<MoneyToMinorUnitsConverter>();

    /// <summary>Variante pour une propriete monetaire facultative.</summary>
    public static PropertyBuilder<decimal?> HasMoneyConversion(this PropertyBuilder<decimal?> builder) =>
        builder.HasConversion(
            amount => amount.HasValue
                ? (long?)Math.Round(amount.Value * MoneyToMinorUnitsConverter.Factor, MidpointRounding.AwayFromZero)
                : null,
            minorUnits => minorUnits.HasValue
                ? minorUnits.Value / MoneyToMinorUnitsConverter.Factor
                : null);
}
