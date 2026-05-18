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
var req = AppendStreamRequest.For(stream)
    .AddMessage(new OrderPlaced("ORD-001", "Alice", 3, DateTimeOffset.UtcNow))
    .AddMessage(new OrderPaid("ORD-001", 29.97m, DateTimeOffset.UtcNow))
    .AddMessage(new OrderShipped("ORD-001", "TRACK-9876", DateTimeOffset.UtcNow))
    .ExpectedState(ExpectedStreamState.NoStream)
    .Build();

var result = await client.Streams.AppendStream(req);

Log.Information("Appended {} events to {Stream} at revision {Revision}", req.Records.Length,  stream, result.Revision);

// Read them back
await foreach (var message in client.Streams.ReadStreamRecords(stream)) {
    message.Switch(
        record => Log.Information(record.ToString()),
        _ => { });
}

record OrderPlaced(string OrderId, string Customer, int ItemCount, DateTimeOffset PlacedAt);
record OrderPaid(string OrderId, decimal Amount, DateTimeOffset PaidAt);
record OrderShipped(string OrderId, string TrackingNumber, DateTimeOffset ShippedAt);


//
// using Kurrent.Client;
// using Kurrent.Client.Streams;
// using Serilog;
// using Serilog.Extensions.Logging;
//
// Log.Logger = new LoggerConfiguration()
//     .MinimumLevel.Information()
//     .WriteTo.Console()
//     .CreateLogger();
//
// var options = KurrentClientOptions.Builder
//     .FromConnectionString("kurrentdb://admin:changeit@localhost:2113?tls=false")
//     .WithLoggerFactory(new SerilogLoggerFactory())
//     .Build();
//
// await using var client = KurrentClient.Create(options);
//
// // await using var client = KurrentClient.Create("kurrentdb://admin:changeit@localhost:2113?tls=false");
//
// var stream = $"order-{Guid.NewGuid().ToString()[..8]}";
//
// // Append events
//
// var req = AppendStreamRequest.For(stream)
//     .AddRecord(new OrderPlaced("ORD-001", "Alice", 3, DateTimeOffset.UtcNow))
//     .AddRecord(new OrderPaid("ORD-001", 29.97m, DateTimeOffset.UtcNow))
//     .AddRecord(new OrderShipped("ORD-001", "TRACK-9876", DateTimeOffset.UtcNow))
//     .Build();
//
// var result = await client.Streams.AppendStream(req);
//
// Log.Information("Appended 3 events to {Stream} at revision {Revision}", stream, result.Revision);
//
// var temp = await client.Streams.ReadStreamRecords(stream).ToListAsync();
//
// // // Read them back
// // await foreach (var message in client.Streams.ReadStreamRecords(stream)) {
// //     message.Switch(
// //         record => Log.Information("[{Revision}] {Type}: {@Value}",
// //             record.Revision, record.ValueType.Name, record.Value),
// //         _ => { });
// // }
//
// record OrderPlaced(string OrderId, string Customer, int ItemCount, DateTimeOffset PlacedAt);
// record OrderPaid(string OrderId, decimal Amount, DateTimeOffset PaidAt);
// record OrderShipped(string OrderId, string TrackingNumber, DateTimeOffset ShippedAt);
