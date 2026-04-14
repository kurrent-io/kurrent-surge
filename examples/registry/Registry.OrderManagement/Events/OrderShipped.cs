namespace Registry.OrderManagement.Events;

public record OrderShipped(
    string OrderId,
    string Carrier,
    string TrackingNumber,
    DateTimeOffset ShippedAt
);
