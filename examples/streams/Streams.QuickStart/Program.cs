using Kurrent.Client;
using Kurrent.Client.Streams;
using Streams.QuickStart.Events;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");

var stream = $"order-{Guid.NewGuid().ToString()[..8]}";

// Append events
var records = new[] {
    AppendRecord.New().Stream(stream).Value(new OrderPlaced("ORD-001", "Alice", 3, DateTimeOffset.UtcNow)).Build(),
    AppendRecord.New().Stream(stream).Value(new OrderPaid("ORD-001", 29.97m, DateTimeOffset.UtcNow)).Build(),
    AppendRecord.New().Stream(stream).Value(new OrderShipped("ORD-001", "TRACK-9876", DateTimeOffset.UtcNow)).Build()
};

var result = await client.Streams.AppendStream(stream, records, ExpectedStreamState.NoStream);

Log.Information("Appended {} events to {Stream} at revision {Revision}", records.Length, stream, result.Revision);

// Read them back
await foreach (var message in client.Streams.ReadStream(stream)) {
    message.Switch(
        record => Log.Information(record.ToString()),
        _ => { });
}
