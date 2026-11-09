using GestionCommandes.CatalogService.Data;
using GestionCommandes.CatalogService.Domain;
using GestionCommandes.CatalogService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.CatalogService.Controllers;

/// <summary>
/// Categories du catalogue. La lecture est ouverte a tout compte connecte ;
/// la modification est reservee au back-office.
/// </summary>
[ApiController]
[Route("api/categories")]
[Authorize]
public sealed class CategoriesController(CatalogDbContext context) : ControllerBase
{
    /// <summary>Liste les categories.</summary>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryResponse>>> List(CancellationToken cancellationToken)
    {
        var categories = await context.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                IsActive = c.IsActive,
                ProductCount = c.Products.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    /// <summary>Renvoie une categorie.</summary>
    /// <param name="id">Identifiant de la categorie.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var category = await context.Categories
            .Where(c => c.Id == id)
            .Select(c => new CategoryResponse
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                IsActive = c.IsActive,
                ProductCount = c.Products.Count
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("La categorie", id);

        return Ok(category);
    }

    /// <summary>Cree une categorie.</summary>
    /// <param name="request">Code et libelle.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CategoryResponse>> Create(
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Categories.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"La categorie '{code}' existe deja.");
        }

        var category = new Category { Code = code, Name = request.Name.Trim() };

        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = category.Id }, new CategoryResponse
        {
            Id = category.Id,
            Code = category.Code,
            Name = category.Name,
            IsActive = category.IsActive,
            ProductCount = 0
        });
    }

    /// <summary>Met a jour une categorie.</summary>
    /// <param name="id">Identifiant de la categorie.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CategoryResponse>> Update(
        Guid id,
        CategoryRequest request,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("La categorie", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Categories.AnyAsync(c => c.Id != id && c.Code == code, cancellationToken))
        {
            throw new ConflictException($"La categorie '{code}' existe deja.");
        }

        category.Code = code;
        category.Name = request.Name.Trim();

        await context.SaveChangesAsync(cancellationToken);

        return await Get(id, cancellationToken);
    }

    /// <summary>Active ou desactive une categorie.</summary>
    /// <param name="id">Identifiant de la categorie.</param>
    /// <param name="request">Nouvel etat.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/active")]
    [Authorize(Policy = Policies.BackOffice)]
    public async Task<ActionResult<CategoryResponse>> SetActive(
        Guid id,
        SetActiveRequest request,
        CancellationToken cancellationToken)
    {
        var category = await context.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw new NotFoundException("La categorie", id);

        category.IsActive = request.IsActive;
        await context.SaveChangesAsync(cancellationToken);

        return await Get(id, cancellationToken);
    }
}
