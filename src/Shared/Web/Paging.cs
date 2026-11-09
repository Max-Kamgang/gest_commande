using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.Shared.Web;

/// <summary>Parametres de pagination communs a toutes les listes.</summary>
public class PageQuery
{
    private int _pageSize = 20;

    /// <summary>Numero de page, base 1.</summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>Taille de page : 1 a 100, 20 par defaut.</summary>
    [Range(1, 100)]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? 20 : Math.Min(value, 100);
    }

    /// <summary>Elements a ignorer.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Page de resultats renvoyee par les endpoints de liste.</summary>
/// <typeparam name="T">Type des elements.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Elements de la page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Numero de page.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Taille de page.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Nombre total d'elements correspondant au filtre.</summary>
    public int TotalCount { get; init; }

    /// <summary>Nombre total de pages.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

/// <summary>Pagination d'une requete EF Core.</summary>
public static class PagingExtensions
{
    /// <summary>Compte le total, extrait la page demandee et projette chaque element.</summary>
    public static async Task<PagedResult<TOut>> ToPageAsync<TIn, TOut>(
        this IQueryable<TIn> source,
        PageQuery query,
        Func<TIn, TOut> projection,
        CancellationToken cancellationToken = default)
    {
        var total = await source.CountAsync(cancellationToken);

        List<TIn> items = total == 0
            ? []
            : await source.Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);

        return new PagedResult<TOut>
        {
            Items = items.Select(projection).ToList(),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total
        };
    }
}
