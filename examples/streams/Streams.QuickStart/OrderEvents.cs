using Kurrent.Surge.Schema.Catalogs;

// Declared once: a namespace scan turns these records into a message catalog, and AlwaysActive
// registers it into every client automatically — no UseCatalog call needed. The source generator
// (shipped inside the Kurrent.Surge.Core package) produces the catalog from this single attribute.
[assembly: MessageCatalog("Streams.QuickStart.Events", Key = "orders", AlwaysActive = true)]

namespace Streams.QuickStart.Events;

public record OrderPlaced(string OrderId, string Customer, int ItemCount, DateTimeOffset PlacedAt);

public record OrderPaid(string OrderId, decimal Amount, DateTimeOffset PaidAt);

public record OrderShipped(string OrderId, string TrackingNumber, DateTimeOffset ShippedAt);
