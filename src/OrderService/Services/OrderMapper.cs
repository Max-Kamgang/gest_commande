using GestionCommandes.OrderService.Domain;
using GestionCommandes.OrderService.Dtos;

namespace GestionCommandes.OrderService.Services;

/// <summary>Projection des entites vers les DTO de l'API.</summary>
public static class OrderMapper
{
    /// <summary>Projette un client.</summary>
    public static CustomerResponse ToResponse(Customer customer) => new()
    {
        Id = customer.Id,
        Code = customer.Code,
        Name = customer.Name,
        Email = customer.Email,
        Phone = customer.Phone,
        City = customer.City,
        Address = customer.Address,
        IsActive = customer.IsActive,
        OrderCount = customer.Orders.Count,
        CreatedAt = customer.CreatedAt,
        UpdatedAt = customer.UpdatedAt
    };

    /// <summary>Projette une commande, en exposant les transitions encore possibles.</summary>
    public static OrderResponse ToResponse(Order order) => new()
    {
        Id = order.Id,
        Number = order.Number,
        CustomerId = order.CustomerId,
        CustomerName = order.Customer?.Name,
        CustomerCode = order.Customer?.Code,
        Status = order.Status,
        AllowedTransitions = OrderStateMachine.NextStates(order.Status).ToList(),
        ShippingAddress = order.ShippingAddress,
        ShippingCity = order.ShippingCity,
        Notes = order.Notes,
        SubTotal = order.SubTotal,
        VatRate = order.VatRate,
        VatAmount = order.VatAmount,
        Total = order.Total,
        Currency = order.Currency,
        LastError = order.LastError,
        LastErrorAt = order.LastErrorAt,
        DeliveryId = order.DeliveryId,
        CreatedBy = order.CreatedBy,
        CreatedAt = order.CreatedAt,
        ConfirmedAt = order.ConfirmedAt,
        PreparedAt = order.PreparedAt,
        ShippedAt = order.ShippedAt,
        DeliveredAt = order.DeliveredAt,
        CancelledAt = order.CancelledAt,
        CancellationReason = order.CancellationReason,
        Lines = order.Lines
            .OrderBy(l => l.Id)
            .Select(l => new OrderLineResponse
            {
                Id = l.Id,
                ProductId = l.ProductId,
                Sku = l.Sku,
                ProductName = l.ProductName,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice,
                LineTotal = l.LineTotal,
                PricedAt = l.PricedAt
            })
            .ToList()
    };

    /// <summary>Projette une entree d'historique.</summary>
    public static OrderHistoryResponse ToResponse(OrderStatusHistoryEntry entry) => new()
    {
        FromStatus = entry.FromStatus,
        ToStatus = entry.ToStatus,
        Reason = entry.Reason,
        ChangedBy = entry.ChangedBy,
        ChangedAt = entry.ChangedAt
    };
}
