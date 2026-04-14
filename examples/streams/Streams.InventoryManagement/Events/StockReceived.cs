namespace Streams.InventoryManagement.Events;

/// <summary>
/// Event representing stock received into a warehouse.
/// </summary>
/// <param name="Sku">Stock keeping unit identifier.</param>
/// <param name="Quantity">Number of units received.</param>
/// <param name="Warehouse">Warehouse where stock was received.</param>
/// <param name="PurchaseOrderId">Associated purchase order.</param>
/// <param name="ReceivedAt">When the stock was received.</param>
public record StockReceived(
    string Sku,
    int Quantity,
    string Warehouse,
    string PurchaseOrderId,
    DateTimeOffset ReceivedAt
);
