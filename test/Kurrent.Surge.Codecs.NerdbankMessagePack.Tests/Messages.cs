using PolyType;

namespace Kurrent.Surge.Codecs.NerdbankMessagePack.Tests;

public record OrderPlaced(Guid OrderId, string CustomerName, decimal Amount, DateTimeOffset PlacedAt);

public record OrderShipped(Guid OrderId, string TrackingNumber, DateTimeOffset ShippedAt);

public record InventoryAdjusted(string Sku, int Delta);

[GenerateShapeFor<OrderPlaced>]
[GenerateShapeFor<OrderShipped>]
[GenerateShapeFor<InventoryAdjusted>]
partial class MessageWitness;
