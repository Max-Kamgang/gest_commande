using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace GestionCommandes.BuildingBlocks.Data;

/// <summary>
/// SQLite ne sait pas trier ni comparer un <see cref="DateTimeOffset"/>
/// (« SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses »).
/// On le stocke donc en <b>millisecondes Unix UTC</b> : les <c>ORDER BY</c>, les
/// filtres de periode et les index fonctionnent alors nativement.
///
/// Toutes les dates de la plateforme sont manipulees en UTC : la relecture renvoie
/// un <see cref="DateTimeOffset"/> d'offset zero, ce qui est le comportement voulu.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime().ToUnixTimeMilliseconds(),
            value => DateTimeOffset.FromUnixTimeMilliseconds(value))
    {
    }
}

/// <summary>Variante pour les dates facultatives.</summary>
public sealed class NullableUtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset?, long?>
{
    public NullableUtcDateTimeOffsetConverter()
        : base(
            value => value.HasValue ? value.Value.ToUniversalTime().ToUnixTimeMilliseconds() : null,
            value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null)
    {
    }
}
