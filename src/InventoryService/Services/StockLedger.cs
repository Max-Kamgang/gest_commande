using GestionCommandes.BuildingBlocks.Events;
using GestionCommandes.BuildingBlocks.Security;
using GestionCommandes.InventoryService.Data;
using GestionCommandes.InventoryService.Domain;

namespace GestionCommandes.InventoryService.Services;

/// <summary>
/// Operations elementaires partagees par la gestion de stock et les reservations :
/// journalisation des mouvements et emission de l'alerte « stock bas ».
/// </summary>
public sealed class StockLedger(
    InventoryDbContext context,
    ICurrentUser currentUser,
    IEventPublisher events,
    ILogger<StockLedger> logger)
{
    /// <summary>Ajoute un mouvement au journal, apres application sur la ligne de stock.</summary>
    public StockMovement RecordMovement(
        StockItem item,
        MovementType type,
        int quantity,
        string? reference,
        string? reason)
    {
        var movement = new StockMovement
        {
            StockItemId = item.Id,
            ProductId = item.ProductId,
            WarehouseId = item.WarehouseId,
            Type = type,
            Quantity = quantity,
            QuantityOnHandAfter = item.QuantityOnHand,
            QuantityReservedAfter = item.QuantityReserved,
            Reference = reference?.Trim(),
            Reason = reason?.Trim(),
            PerformedBy = currentUser.UserName ?? "service-interne",
            OccurredAt = DateTimeOffset.UtcNow
        };

        context.StockMovements.Add(movement);

        return movement;
    }

    /// <summary>
    /// Emet un evenement <c>inventory.stock-low</c> si le disponible est tombe au
    /// niveau du seuil ou en dessous. Publication fire-and-forget : une panne de
    /// notification-service ne bloque jamais le mouvement de stock.
    /// </summary>
    public void NotifyIfLowStock(StockItem item, string? reference = null)
    {
        if (!item.IsLowStock)
        {
            return;
        }

        // Le libelle vient de la denormalisation alimentee par l'appelant ;
        // a defaut on cite l'identifiant du produit, jamais une table partagee.
        var label = string.IsNullOrWhiteSpace(item.ProductLabel)
            ? $"produit {item.ProductId}"
            : item.ProductLabel;

        var warehouse = item.Warehouse?.Name ?? item.WarehouseId.ToString();

        logger.LogWarning(
            "Stock bas : {ProductId} ({Label}) dans {Warehouse} - disponible {Available}, seuil {Threshold}",
            item.ProductId,
            label,
            warehouse,
            item.QuantityAvailable,
            item.LowStockThreshold);

        events.Publish(new IntegrationEvent
        {
            EventType = EventTypes.StockLow,
            Subject = item.ProductId.ToString(),
            Title = $"Stock bas : {label}",
            Message =
                $"Le {label} n'a plus que {item.QuantityAvailable} unite(s) disponible(s) " +
                $"dans l'entrepot {warehouse} (seuil d'alerte : {item.LowStockThreshold}). " +
                "Un reapprovisionnement est a prevoir.",
            Severity = item.QuantityAvailable <= 0 ? EventSeverities.Critical : EventSeverities.Warning,
            Channel = EventChannels.Email,
            Recipient = "gestion@micro.local",
            Data = new Dictionary<string, string>
            {
                ["productId"] = item.ProductId.ToString(),
                ["productLabel"] = item.ProductLabel ?? string.Empty,
                ["warehouseId"] = item.WarehouseId.ToString(),
                ["available"] = item.QuantityAvailable.ToString(),
                ["onHand"] = item.QuantityOnHand.ToString(),
                ["reserved"] = item.QuantityReserved.ToString(),
                ["threshold"] = item.LowStockThreshold.ToString(),
                ["reference"] = reference ?? string.Empty
            }
        });
    }
}
