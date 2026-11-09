using GestionCommandes.BuildingBlocks.Paging;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.BuildingBlocks.Paging;

/// <summary>Helpers de pagination EF Core.</summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Compte puis extrait une page, et projette chaque element via <paramref name="projection"/>.
    /// Un seul aller-retour logique pour le comptage, un pour la page.
    /// </summary>
    public static async Task<PagedResult<TOut>> ToPagedResultAsync<TIn, TOut>(
        this IQueryable<TIn> source,
        PageQuery query,
        Func<TIn, TOut> projection,
        CancellationToken cancellationToken = default)
    {
        var total = await source.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<TOut>.Empty(query.Page, query.PageSize);
        }

        var items = await source
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<TOut>.Create(
            items.Select(projection).ToList(),
            query.Page,
            query.PageSize,
            total);
    }
}
