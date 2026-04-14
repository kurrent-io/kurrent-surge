namespace Streams.InventoryManagement.Events;

/// <summary>
/// Event representing a manual stock adjustment (positive or negative).
/// </summary>
/// <param name="Sku">Stock keeping unit identifier.</param>
/// <param name="Quantity">Adjustment amount (positive = add, negative = remove).</param>
/// <param name="Reason">Reason for the adjustment.</param>
/// <param name="AdjustedAt">When the adjustment was made.</param>
public record StockAdjusted(
    string Sku,
    int Quantity,
    string Reason,
    DateTimeOffset AdjustedAt
);
