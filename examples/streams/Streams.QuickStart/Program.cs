using Kurrent.Surge;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Streams.QuickStart.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var services = new ServiceCollection()
    .AddKurrentSurge(surge => surge.Connection("kurrentdb://admin:changeit@localhost:2113?tls=false"));

await using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<KurrentClient>();

var stream = $"order-{Guid.NewGuid().ToString()[..8]}";

// Append events
var records = new[] {
    AppendRecord.Create(new OrderPlaced("ORD-001", "Alice", 3, DateTimeOffset.UtcNow), r => r with { Stream = stream }),
    AppendRecord.Create(new OrderPaid("ORD-001", 29.97m, DateTimeOffset.UtcNow), r => r with { Stream = stream }),
    AppendRecord.Create(new OrderShipped("ORD-001", "TRACK-9876", DateTimeOffset.UtcNow), r => r with { Stream = stream })
};

var result = await client.Streams.AppendStream(stream, records, ExpectedStreamState.NoStream);

Log.Information("Appended {Count} events to {Stream} at revision {Revision}", records.Length, stream, result.Revision);

// Read them back
await foreach (var message in client.Streams.ReadStream(stream)) {
    message.Switch(
        record => Log.Information(record.ToString()),
        _ => { });
}
