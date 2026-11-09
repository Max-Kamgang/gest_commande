using GestionCommandes.BuildingBlocks.Errors;
using GestionCommandes.BuildingBlocks.Paging;
using GestionCommandes.OrderService.Data;
using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionCommandes.OrderService.Services;

/// <summary>Gestion des clients.</summary>
public sealed class CustomerManager(OrderDbContext context, ILogger<CustomerManager> logger)
{
    /// <summary>Liste paginee des clients.</summary>
    public async Task<PagedResult<CustomerResponse>> SearchAsync(
        CustomerQuery query,
        CancellationToken cancellationToken)
    {
        var source = context.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c =>
                EF.Functions.Like(c.Code, $"%{term}%") ||
                EF.Functions.Like(c.Name, $"%{term}%") ||
                EF.Functions.Like(c.Email, $"%{term}%") ||
                EF.Functions.Like(c.City, $"%{term}%"));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(c => c.IsActive == isActive);
        }

        return await source
            .OrderBy(c => c.Name)
            .ToPagedResultAsync(query, OrderMapper.ToResponse, cancellationToken);
    }

    /// <summary>Recupere un client.</summary>
    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .AsNoTracking()
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        return OrderMapper.ToResponse(customer);
    }

    /// <summary>Cree un client.</summary>
    public async Task<CustomerResponse> CreateAsync(CustomerRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Customers.AnyAsync(c => c.Code == code, cancellationToken))
        {
            throw new ConflictException($"Un client porte deja le code '{code}'.", "CUSTOMER_CODE_TAKEN");
        }

        var customer = new Customer
        {
            Code = code,
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Phone = request.Phone?.Trim(),
            City = request.City.Trim(),
            Address = request.Address?.Trim(),
            IsActive = true
        };

        context.Customers.Add(customer);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Client {Code} ({Name}) cree", code, customer.Name);

        return OrderMapper.ToResponse(customer);
    }

    /// <summary>Met a jour un client.</summary>
    public async Task<CustomerResponse> UpdateAsync(
        Guid id,
        CustomerRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        var code = request.Code.Trim().ToUpperInvariant();

        if (await context.Customers.AnyAsync(c => c.Code == code && c.Id != id, cancellationToken))
        {
            throw new ConflictException($"Un autre client porte deja le code '{code}'.", "CUSTOMER_CODE_TAKEN");
        }

        customer.Code = code;
        customer.Name = request.Name.Trim();
        customer.Email = request.Email.Trim().ToLowerInvariant();
        customer.Phone = request.Phone?.Trim();
        customer.City = request.City.Trim();
        customer.Address = request.Address?.Trim();
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return OrderMapper.ToResponse(customer);
    }

    /// <summary>Active ou desactive un client.</summary>
    public async Task<CustomerResponse> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        customer.IsActive = isActive;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Client {Code} {Etat}", customer.Code, isActive ? "active" : "desactive");

        return OrderMapper.ToResponse(customer);
    }

    /// <summary>Supprime un client sans commande.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await context.Customers
            .Include(c => c.Orders)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Le client", id);

        if (customer.Orders.Count > 0)
        {
            throw new ConflictException(
                $"Le client '{customer.Code}' porte {customer.Orders.Count} commande(s) : " +
                "desactivez-le au lieu de le supprimer, afin de conserver l'historique.",
                "CUSTOMER_HAS_ORDERS",
                new Dictionary<string, object?> { ["orderCount"] = customer.Orders.Count });
        }

        context.Customers.Remove(customer);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogWarning("Client {Code} supprime", customer.Code);
    }
}
