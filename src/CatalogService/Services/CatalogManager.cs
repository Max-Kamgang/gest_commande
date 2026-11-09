using GestionCommandes.BuildingBlocks.Domain;
using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.CatalogService.Data;
using GestionCommandes.CatalogService.Domain;
using GestionCommandes.CatalogService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Services;

/// <summary>Logique metier du catalogue : categories, produits et tarification.</summary>
public sealed class CatalogManager(
    CatalogDbContext context,
    ICurrentUser currentUser,
    ILogger<CatalogManager> logger)
{
    // ----------------------------- Categories -----------------------------

    /// <summary>Liste paginee des categories.</summary>
    public async Task<PagedResult<CategoryResponse>> SearchCategoriesAsync(
        CategoryQuery query,
        CancellationToken cancellationToken)
    {
        var source = context.Categories
            .AsNoTracking()
            .Include(c => c.Products)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c =>
                EF.Functions.Like(c.Code, $"%{term}%") ||
                EF.Functions.Like(c.Name, $"%{term}%"));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(c => c.IsActive == isActive);
        }

        return await source
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere une categorie.</summary>
    public async Task<CategoryResponse> GetCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .AsNoTracking()
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("La categorie", id);

        return ToResponse(category);
    }

    /// <summary>Cree une categorie.</summary>
    public async Task<CategoryResponse> CreateCategoryAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Categories.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Une categorie porte deja le code '{code}'.", "CATEGORY_CODE_TAKEN");
        }

        var category = new Category
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Categorie {Code} creee par {User}", code, currentUser.UserName ?? "inconnu");

        return ToResponse(category);
    }

    /// <summary>Met a jour une categorie.</summary>
    public async Task<CategoryResponse> UpdateCategoryAsync(
        Guid id,
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("La categorie", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Categories.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            throw new ConflictException($"Une autre categorie porte deja le code '{code}'.", "CATEGORY_CODE_TAKEN");
        }

        category.Code = code;
        category.Name = request.Name.Trim();
        category.Description = request.Description?.Trim();
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return ToResponse(category);
    }

    /// <summary>Active ou desactive une categorie (et masque ses produits du catalogue public).</summary>
    public async Task<CategoryResponse> SetCategoryActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("La categorie", id);

        category.IsActive = isActive;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Categorie {Code} {Etat} par {User}",
            category.Code,
            isActive ? "activee" : "desactivee",
            currentUser.UserName ?? "inconnu");

        return ToResponse(category);
    }

    /// <summary>Supprime une categorie vide.</summary>
    public async Task DeleteCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .Include(c => c.Products)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("La categorie", id);

        if (category.Products.Count > 0)
        {
            throw new ConflictException(
                $"La categorie '{category.Code}' contient {category.Products.Count} produit(s). " +
                "Deplacez ou supprimez ces produits avant de la supprimer, ou desactivez-la.",
                "CATEGORY_NOT_EMPTY",
                new Dictionary<string, object?> { ["productCount"] = category.Products.Count });
        }

        context.Categories.Remove(category);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Categorie {Code} supprimee par {User}", category.Code, currentUser.UserName ?? "inconnu");
    }

    // ------------------------------ Produits ------------------------------

    /// <summary>Liste paginee et filtree des produits.</summary>
    public async Task<PagedResult<ProductResponse>> SearchProductsAsync(
        ProductQuery query,
        CancellationToken cancellationToken)
    {
        if (query.MinPrice is { } min && query.MaxPrice is { } max && min > max)
        {
            throw new BadRequestException("minPrice ne peut pas etre superieur a maxPrice.", "INVALID_PRICE_RANGE");
        }

        var source = context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(p =>
                EF.Functions.Like(p.Sku, $"%{term}%") ||
                EF.Functions.Like(p.Name, $"%{term}%") ||
                (p.Description != null && EF.Functions.Like(p.Description, $"%{term}%")));
        }

        if (query.CategoryId is { } categoryId)
        {
            source = source.Where(p => p.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(p => p.IsActive == isActive);
        }

        if (query.MinPrice is { } minPrice)
        {
            source = source.Where(p => p.UnitPrice >= minPrice);
        }

        if (query.MaxPrice is { } maxPrice)
        {
            source = source.Where(p => p.UnitPrice <= maxPrice);
        }

        source = ApplySort(source, query.Sort);

        return await source.ToPagedResultAsync(query, ToResponse, cancellationToken);
    }

    /// <summary>Recupere un produit par identifiant.</summary>
    public async Task<ProductResponse> GetProductAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        return ToResponse(product);
    }

    /// <summary>Recupere un produit par reference article.</summary>
    public async Task<ProductResponse> GetProductBySkuAsync(string sku, CancellationToken cancellationToken)
    {
        var normalized = sku.Trim();

        var product = await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Sku == normalized, cancellationToken)
            ?? throw new NotFoundException("Le produit de reference", sku);

        return ToResponse(product);
    }

    /// <summary>Cree un produit.</summary>
    public async Task<ProductResponse> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();

        if (await context.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new ConflictException($"Un produit porte deja la reference '{sku}'.", "SKU_TAKEN");
        }

        var category = await context.Categories
            .FirstOrDefaultAsync(c => c.Id == request.CategoryId, cancellationToken)
            ?? throw new BadRequestException(
                $"La categorie '{request.CategoryId}' est inconnue.",
                "UNKNOWN_CATEGORY");

        var product = new Product
        {
            Sku = sku,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CategoryId = category.Id,
            UnitPrice = request.UnitPrice,
            Currency = DemoData.Currency,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit.Trim(),
            IsActive = true
        };

        context.Products.Add(product);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Produit {Sku} cree a {Price} {Currency} par {User}",
            sku,
            product.UnitPrice,
            product.Currency,
            currentUser.UserName ?? "inconnu");

        product.Category = category;

        return ToResponse(product);
    }

    /// <summary>Met a jour un produit (hors prix).</summary>
    public async Task<ProductResponse> UpdateProductAsync(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        if (product.CategoryId != request.CategoryId &&
            !await context.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new BadRequestException($"La categorie '{request.CategoryId}' est inconnue.", "UNKNOWN_CATEGORY");
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.CategoryId = request.CategoryId;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit.Trim();
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return await GetProductAsync(id, cancellationToken);
    }

    /// <summary>Change le prix d'un produit et trace le changement.</summary>
    public async Task<ProductResponse> ChangePriceAsync(
        Guid id,
        ChangePriceRequest request,
        CancellationToken cancellationToken)
    {
        var product = await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        if (product.UnitPrice == request.UnitPrice)
        {
            throw new ConflictException(
                $"Le produit '{product.Sku}' est deja au prix de {request.UnitPrice} {product.Currency}.",
                "PRICE_UNCHANGED");
        }

        var oldPrice = product.UnitPrice;

        product.PriceHistory.Add(new PriceHistoryEntry
        {
            ProductId = product.Id,
            OldPrice = oldPrice,
            NewPrice = request.UnitPrice,
            ChangedBy = currentUser.UserName ?? "systeme",
            Reason = request.Reason?.Trim()
        });

        product.UnitPrice = request.UnitPrice;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Prix du produit {Sku} modifie de {Old} a {New} {Currency} par {User}",
            product.Sku,
            oldPrice,
            product.UnitPrice,
            product.Currency,
            currentUser.UserName ?? "inconnu");

        return ToResponse(product);
    }

    /// <summary>Historique des prix d'un produit.</summary>
    public async Task<IReadOnlyList<PriceHistoryResponse>> GetPriceHistoryAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await context.Products.AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new NotFoundException("Le produit", id);
        }

        return await context.PriceHistory
            .AsNoTracking()
            .Where(h => h.ProductId == id)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new PriceHistoryResponse
            {
                OldPrice = h.OldPrice,
                NewPrice = h.NewPrice,
                ChangedBy = h.ChangedBy,
                Reason = h.Reason,
                ChangedAt = h.ChangedAt
            })
            .ToListAsync(cancellationToken);
    }

    /// <summary>Active ou desactive un produit.</summary>
    public async Task<ProductResponse> SetProductActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var product = await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        product.IsActive = isActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Produit {Sku} {Etat} par {User}",
            product.Sku,
            isActive ? "active" : "desactive",
            currentUser.UserName ?? "inconnu");

        return ToResponse(product);
    }

    /// <summary>Supprime un produit.</summary>
    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Le produit", id);

        context.Products.Remove(product);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Produit {Sku} supprime par {User}", product.Sku, currentUser.UserName ?? "inconnu");
    }

    // ----------------------------- Tarification -----------------------------

    /// <summary>
    /// Renvoie les prix courants des produits demandes. Appele par order-service
    /// a la confirmation d'une commande (etape 1 de la saga). Les produits inconnus
    /// sont listes dans <c>missing</c> plutot que de faire echouer tout l'appel :
    /// c'est order-service qui decide de la suite.
    /// </summary>
    public async Task<PricingResponse> GetPricingAsync(PricingRequest request, CancellationToken cancellationToken)
    {
        var ids = request.ProductIds.Distinct().ToList();

        var products = await context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var items = products.Select(p => new PricingItem
        {
            ProductId = p.Id,
            Sku = p.Sku,
            Name = p.Name,
            UnitPrice = p.UnitPrice,
            Unit = p.Unit,
            // Un produit dont la categorie est desactivee n'est pas commandable.
            IsActive = p.IsActive && (p.Category?.IsActive ?? true)
        }).ToList();

        var missing = ids.Except(products.Select(p => p.Id)).ToList();

        logger.LogInformation(
            "Tarification demandee pour {Requested} produit(s) : {Found} trouve(s), {Missing} inconnu(s)",
            ids.Count,
            items.Count,
            missing.Count);

        return new PricingResponse
        {
            Currency = DemoData.Currency,
            Items = items,
            Missing = missing,
            QuotedAt = DateTimeOffset.UtcNow
        };
    }

    // ------------------------------- Helpers -------------------------------

    private static IQueryable<Product> ApplySort(IQueryable<Product> source, string? sort) => sort?.ToLowerInvariant() switch
    {
        "price" => source.OrderBy(p => p.UnitPrice),
        "price_desc" => source.OrderByDescending(p => p.UnitPrice),
        "sku" => source.OrderBy(p => p.Sku),
        "sku_desc" => source.OrderByDescending(p => p.Sku),
        "createdat" => source.OrderBy(p => p.CreatedAt),
        "createdat_desc" => source.OrderByDescending(p => p.CreatedAt),
        "name_desc" => source.OrderByDescending(p => p.Name),
        _ => source.OrderBy(p => p.Name)
    };

    private static CategoryResponse ToResponse(Category category) => new()
    {
        Id = category.Id,
        Code = category.Code,
        Name = category.Name,
        Description = category.Description,
        IsActive = category.IsActive,
        ProductCount = category.Products.Count,
        CreatedAt = category.CreatedAt,
        UpdatedAt = category.UpdatedAt
    };

    private static ProductResponse ToResponse(Product product) => new()
    {
        Id = product.Id,
        Sku = product.Sku,
        Name = product.Name,
        Description = product.Description,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        UnitPrice = product.UnitPrice,
        Currency = product.Currency,
        Unit = product.Unit,
        IsActive = product.IsActive,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt
    };
}
