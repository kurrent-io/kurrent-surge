using Kurrent.Client;
using Kurrent.Client.Streams;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");

var stream = $"order-{Guid.NewGuid().ToString()[..8]}";

// Append events
var result = await client.Streams.AppendStream(stream, [
    AppendRecord.New.WithValue(new OrderPlaced("ORD-001", "Alice", 3, DateTimeOffset.UtcNow)).Build(),
    AppendRecord.New.WithValue(new OrderPaid("ORD-001", 29.97m, DateTimeOffset.UtcNow)).Build(),
    AppendRecord.New.WithValue(new OrderShipped("ORD-001", "TRACK-9876", DateTimeOffset.UtcNow)).Build()
]);

Log.Information("Appended 3 events to {Stream} at revision {Revision}", stream, result.Revision);

// Read them back
await using var read = await client.Streams.ReadStreamRecords(stream);

await foreach (var message in read) {
    message.Switch(
        record => Log.Information("[{Revision}] {Type}: {@Value}",
            record.Revision, record.Value?.GetType().Name, record.Value),
        _ => { });
}

record OrderPlaced(string OrderId, string Customer, int ItemCount, DateTimeOffset PlacedAt);
record OrderPaid(string OrderId, decimal Amount, DateTimeOffset PaidAt);
record OrderShipped(string OrderId, string TrackingNumber, DateTimeOffset ShippedAt);
