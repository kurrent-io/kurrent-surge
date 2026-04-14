namespace Streams.InventoryManagement.Events;

/// <summary>
/// Event representing stock shipped out for an order.
/// </summary>
/// <param name="Sku">Stock keeping unit identifier.</param>
/// <param name="Quantity">Number of units shipped.</param>
/// <param name="OrderId">Customer order being fulfilled.</param>
/// <param name="ShippedAt">When the stock was shipped.</param>
public record StockShipped(
    string Sku,
    int Quantity,
    string OrderId,
    DateTimeOffset ShippedAt
);
