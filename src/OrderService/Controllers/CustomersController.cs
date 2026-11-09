using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using GestionCommandes.Shared.Errors;
using GestionCommandes.Shared.Security;
using GestionCommandes.Shared.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Controllers;

/// <summary>Fichier clients. Reserve au back-office.</summary>
[ApiController]
[Route("api/customers")]
[Authorize(Policy = Policies.BackOffice)]
public sealed class CustomersController(OrderDbContext context) : ControllerBase
{
    /// <summary>Liste paginee des clients.</summary>
    /// <param name="query">Filtres et pagination.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerResponse>>> List(
        [FromQuery] CustomerQuery query,
        CancellationToken cancellationToken)
    {
        var customers = context.Customers.Include(c => c.Orders).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            customers = customers.Where(c =>
                EF.Functions.Like(c.Code, $"%{search}%")
                || EF.Functions.Like(c.Name, $"%{search}%")
                || EF.Functions.Like(c.Email, $"%{search}%"));
        }

        if (query.IsActive is { } isActive)
        {
            customers = customers.Where(c => c.IsActive == isActive);
        }

        var page = await customers
            .OrderBy(c => c.Name)
            .ToPageAsync(query, ToResponse, cancellationToken);

        return Ok(page);
    }

    /// <summary>Renvoie un client.</summary>
    /// <param name="id">Identifiant du client.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        return Ok(ToResponse(customer));
    }

    /// <summary>Cree un client.</summary>
    /// <param name="request">Donnees du client.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Le client '{code}' existe deja.");
        }

        var customer = new Customer
        {
            Code = code,
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            City = request.City.Trim(),
            Address = request.Address?.Trim()
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = customer.Id }, ToResponse(customer));
    }

    /// <summary>Met a jour un client.</summary>
    /// <param name="id">Identifiant du client.</param>
    /// <param name="request">Nouvelles valeurs.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerResponse>> Update(
        Guid id,
        CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Customers.AnyAsync(c => c.Id != id && c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Le client '{code}' existe deja.");
        }

        customer.Code = code;
        customer.Name = request.Name.Trim();
        customer.Email = request.Email.Trim().ToLowerInvariant();
        customer.Phone = request.Phone?.Trim();
        customer.City = request.City.Trim();
        customer.Address = request.Address?.Trim();

        await context.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(customer));
    }

    /// <summary>Active ou desactive un client.</summary>
    /// <param name="id">Identifiant du client.</param>
    /// <param name="isActive">Nouvel etat.</param>
    /// <param name="cancellationToken">Jeton d'annulation.</param>
    [HttpPatch("{id:guid}/active")]
    public async Task<ActionResult<CustomerResponse>> SetActive(
        Guid id,
        [FromQuery] bool isActive,
        CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        customer.IsActive = isActive;

        await context.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(customer));
    }

    private static CustomerResponse ToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        Code = customer.Code,
        Name = customer.Name,
        Email = customer.Email,
        Phone = customer.Phone,
        City = customer.City,
        Address = customer.Address,
        IsActive = customer.IsActive,
        OrderCount = customer.Orders.Count
    };
}
