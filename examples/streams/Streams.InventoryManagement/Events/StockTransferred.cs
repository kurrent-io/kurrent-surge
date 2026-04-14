namespace Streams.InventoryManagement.Events;

/// <summary>
/// Event representing stock moved between warehouses.
/// </summary>
/// <param name="Sku">Stock keeping unit identifier.</param>
/// <param name="Quantity">Number of units transferred.</param>
/// <param name="FromWarehouse">Source warehouse.</param>
/// <param name="ToWarehouse">Destination warehouse.</param>
/// <param name="TransferredAt">When the transfer occurred.</param>
public record StockTransferred(
    string Sku,
    int Quantity,
    string FromWarehouse,
    string ToWarehouse,
    DateTimeOffset TransferredAt
);
