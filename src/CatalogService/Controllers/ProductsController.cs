using GestionCommandes.CatalogService.Data;
using GestionCommandes.CatalogService.Domain;
using GestionCommandes.CatalogService.Dtos;
using GestionCommandes.Shared.Domain;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Controllers;

/// <summary>
/// Produits du catalogue. Tout compte connecte peut consulter ; seul le
/// back-office cree, modifie, revalorise ou desactive.
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController(CatalogDbContext context, ILogger<ProductsController> logger) : ControllerBase
{
    /// <summary>Liste paginee des produits.</summary>
    /// <param name="query">Filtres, tri et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List(
        [FromQuery] ProductQuery query,
        CancellationToken cancellationToken)
    {
        var products = context.Products.Include(p => p.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            products = products.Where(p =>
                EF.Functions.Like(p.Sku, $"%{search}%")
                || EF.Functions.Like(p.Name, $"%{search}%")
                || (p.Description != null && EF.Functions.Like(p.Description, $"%{search}%")));
        }

        if (query.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (query.IsActive is { } isActive)
        {
            products = products.Where(p => p.IsActive == isActive);
        }

        if (query.LowStockOnly == true)
        {
            // Le calcul est fait en SQL : QuantityAvailable est une propriete non mappee.
            products = products.Where(p => p.QuantityOnHand - p.QuantityReserved <= p.LowStockThreshold);
        }

        products = query.Sort switch
        {
            "price" => products.OrderBy(p => p.UnitPrice),
            "price_desc" => products.OrderByDescending(p => p.UnitPrice),
            "sku" => products.OrderBy(p => p.Sku),
            "sku_desc" => products.OrderByDescending(p => p.Sku),
            "available" => products.OrderBy(p => p.QuantityOnHand - p.QuantityReserved),
            "available_desc" => products.OrderByDescending(p => p.QuantityOnHand - p.QuantityReserved),
            "name_desc" => products.OrderByDescending(p => p.Name),
            _ => products.OrderBy(p => p.Name)
        };

        return Ok(await products.ToPageAsync(query, produit => CatalogMapper.ToResponse(produit), cancellationToken));
    }

    /// <summary>Renvoie un produit.</summary>
    /// <param name="id">Identifiant du produit.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var product = await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        return Ok(CatalogMapper.ToResponse(product));
    }

    /// <summary>Cree un produit.</summary>
    /// <param name="request">Donnees du produit, stock initial compris.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim().ToUpperInvariant();

        if (await context.Products.AnyAsync(p => p.Sku == sku, cancellationToken))
        {
            throw new ConflictException($"Le produit '{sku}' existe deja.");
        }

        if (!await context.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new BadRequestException($"La categorie '{request.CategoryId}' est inconnue.");
        }

        var product = new Product
        {
            Sku = sku,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            CategoryId = request.CategoryId,
            UnitPrice = request.UnitPrice,
            Unit = string.IsNullOrWhiteSpace(request.Unit) ? "piece" : request.Unit.Trim(),
            QuantityOnHand = request.QuantityOnHand,
            LowStockThreshold = request.LowStockThreshold
        };

        context.Products.Add(product);

        if (request.QuantityOnHand > 0)
        {
            context.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                Type = MovementType.Entree,
                Quantity = request.QuantityOnHand,
                QuantityOnHandAfter = request.QuantityOnHand,
                QuantityReservedAfter = 0,
                Reason = "Stock initial a la creation du produit",
                PerformedBy = User.Identity?.Name ?? "systeme"
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Produit {Sku} cree", product.Sku);

        return CreatedAtAction(nameof(Get), new { id = product.Id }, CatalogMapper.ToResponse(product));
    }

    /// <summary>Met a jour un produit, hors prix et stock.</summary>
    /// <param name="id">Identifiant du produit.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le produit", id);

        if (!await context.Categories.AnyAsync(c => c.Id == request.CategoryId, cancellationToken))
        {
            throw new BadRequestException($"La categorie '{request.CategoryId}' est inconnue.");
        }

        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.CategoryId = request.CategoryId;
        product.Unit = string.IsNullOrWhiteSpace(request.Unit) ? product.Unit : request.Unit.Trim();
        product.LowStockThreshold = request.LowStockThreshold;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return await Get(id, cancellationToken);
    }

    /// <summary>Revalorise un produit.</summary>
    /// <param name="id">Identifiant du produit.</param>
    /// <param name="request">Nouveau prix.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/price")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> ChangePrice(
        Guid id,
        ChangePriceRequest request,
        CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Le produit", id);

        var previous = product.UnitPrice;

        product.UnitPrice = request.UnitPrice;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        // Les commandes deja confirmees gardent leur prix : il y est fige.
        logger.LogInformation(
            "Prix du produit {Sku} modifie de {Ancien} a {Nouveau}", product.Sku, previous, product.UnitPrice);

        return await Get(id, cancellationToken);
    }

    /// <summary>Active ou desactive un produit.</summary>
    /// <param name="id">Identifiant du produit.</param>
    /// <param name="request">Nouvel etat.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<ProductResponse>> SetActive(
        Guid id,
        SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        var product = await context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("Le produit", id);

        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return await Get(id, cancellationToken);
    }
}

/// <summary>Projections du catalogue vers les formats exposes par l'API.</summary>
public static class CatalogMapper
{
    /// <summary>Convertit un produit, stock compris.</summary>
    /// <param name="product">Produit, categorie chargee si disponible.</param>
    public static ProductResponse ToResponse(Product product) => new()
    {
        Id = product.Id,
        Sku = product.Sku,
        Name = product.Name,
        Description = product.Description,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        UnitPrice = product.UnitPrice,
        Currency = DemoData.Currency,
        Unit = product.Unit,
        IsActive = product.IsActive,
        QuantityOnHand = product.QuantityOnHand,
        QuantityReserved = product.QuantityReserved,
        QuantityAvailable = product.QuantityAvailable,
        LowStockThreshold = product.LowStockThreshold,
        IsLowStock = product.IsLowStock,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt
    };

    /// <summary>Convertit un mouvement de stock.</summary>
    /// <param name="movement">Mouvement, produit charge si disponible.</param>
    public static MovementResponse ToResponse(StockMovement movement) => new()
    {
        Id = movement.Id,
        ProductId = movement.ProductId,
        Sku = movement.Product?.Sku,
        ProductName = movement.Product?.Name,
        Type = movement.Type,
        Quantity = movement.Quantity,
        QuantityOnHandAfter = movement.QuantityOnHandAfter,
        QuantityReservedAfter = movement.QuantityReservedAfter,
        Reference = movement.Reference,
        Reason = movement.Reason,
        PerformedBy = movement.PerformedBy,
        OccurredAt = movement.OccurredAt
    };

    /// <summary>Convertit une reservation.</summary>
    /// <param name="reservation">Reservation, lignes chargees.</param>
    /// <param name="labels">Libelles des produits, par identifiant.</param>
    public static ReservationResponse ToResponse(
        StockReservation reservation,
        IReadOnlyDictionary<Guid, (string Sku, string Name)> labels) => new()
    {
        Id = reservation.Id,
        Reference = reservation.Reference,
        Status = reservation.Status,
        Currency = DemoData.Currency,
        CreatedAt = reservation.CreatedAt,
        ResolvedAt = reservation.ResolvedAt,
        Lines = reservation.Lines.Select(line => new ReservedLineResponse
        {
            ProductId = line.ProductId,
            Sku = labels.TryGetValue(line.ProductId, out var label) ? label.Sku : string.Empty,
            ProductName = labels.TryGetValue(line.ProductId, out var name) ? name.Name : string.Empty,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice
        }).ToList()
    };
}
