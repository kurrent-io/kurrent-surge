namespace Processor.OrderEnrichment.Events;

/// <summary>
/// Event representing an order placed by a customer.
/// </summary>
/// <param name="OrderId">Unique identifier for this order.</param>
/// <param name="CustomerId">Customer who placed the order.</param>
/// <param name="Items">List of items in the order.</param>
/// <param name="PlacedAt">When the order was placed.</param>
public record OrderPlaced(
    string OrderId,
    string CustomerId,
    List<OrderItem> Items,
    DateTimeOffset PlacedAt
);

/// <summary>
/// An item within an order.
/// </summary>
/// <param name="ProductId">Product identifier.</param>
/// <param name="Quantity">Number of units ordered.</param>
/// <param name="UnitPrice">Price per unit at time of order.</param>
public record OrderItem(
    string ProductId,
    int Quantity,
    decimal UnitPrice
);
