using System.ComponentModel.DataAnnotations;

namespace GestionCommandes.BuildingBlocks.Paging;

/// <summary>Parametres de pagination communs a toutes les listes.</summary>
public class PageQuery
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    /// <summary>Numero de page, base 1.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "page doit etre >= 1.")]
    public int Page { get; set; } = 1;

    /// <summary>Taille de page (1 a 100, 20 par defaut).</summary>
    [Range(1, MaxPageSize, ErrorMessage = "pageSize doit etre compris entre 1 et 100.")]
    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value is < 1 ? 20 : Math.Min(value, MaxPageSize);
    }

    /// <summary>Nombre d'elements a ignorer.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Resultat pagine renvoye par les endpoints de liste.</summary>
/// <typeparam name="T">Type des elements.</typeparam>
public sealed class PagedResult<T>
{
    /// <summary>Elements de la page courante.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>Numero de la page courante.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Taille de page demandee.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Nombre total d'elements correspondant au filtre.</summary>
    public int TotalCount { get; init; }

    /// <summary>Nombre total de pages.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>Vrai s'il existe une page suivante.</summary>
    public bool HasNext => Page < TotalPages;

    /// <summary>Vrai s'il existe une page precedente.</summary>
    public bool HasPrevious => Page > 1;

    /// <summary>Fabrique un resultat pagine.</summary>
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new() { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };

    /// <summary>Resultat vide.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalCount = 0 };
}
