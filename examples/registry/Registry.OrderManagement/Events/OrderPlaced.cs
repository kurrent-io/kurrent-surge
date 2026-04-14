namespace Registry.OrderManagement.Events;

public record OrderPlaced(
    string OrderId,
    string CustomerId,
    List<OrderItem> Items,
    decimal Total,
    DateTimeOffset PlacedAt,
    string? ShippingAddress = null,
    string? DiscountCode = null
);

public record OrderItem(string Sku, int Quantity, decimal UnitPrice);
