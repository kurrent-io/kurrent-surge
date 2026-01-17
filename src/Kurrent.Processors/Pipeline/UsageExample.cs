// using System.Text.Json;
// using Kurrent.Processors;
// using Kurrent.Processors.Sinks.Kafka;
// using Microsoft.Extensions.Logging;
//
// namespace Kurrent.Processors.Examples;
//
// // ═══════════════════════════════════════════════════════════════════════════
// // USER'S PROCESSOR - Just emits to collector, doesn't know about sinks
// // ═══════════════════════════════════════════════════════════════════════════
//
// public class OrderProcessor : IRecordProcessor<OrderEvent> {
//     public async ValueTask ProcessRecords(ProcessRecordsContext<OrderEvent> ctx) {
//         foreach (var record in ctx.Records) {
//             if (record.MessageType == "OrderPlaced") {
//                 var order = record.GetData<OrderPlaced>();
//
//                 // Just emit - runtime handles routing to all sinks
//                 // and coordinates checkpoints automatically
//                 await ctx.Collector.EmitAsync(
//                     new OrderEvent(order.OrderId, "processed", order.Total),
//                     ctx.CancellationToken);
//             }
//         }
//     }
//
//     public ValueTask OnCheckpointComplete(CheckpointCompleteContext ctx) {
//         // Optional: log, metrics, etc.
//         Console.WriteLine($"Checkpoint {ctx.CheckpointId} complete");
//         return ValueTask.CompletedTask;
//     }
// }
//
// // ═══════════════════════════════════════════════════════════════════════════
// // STARTUP - Configure sinks via builder
// // ═══════════════════════════════════════════════════════════════════════════
//
// public static class Program {
//     public static async Task Main() {
//         var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
//
//         // At-least-once sink (fire-and-forget)
//         var atLeastOnceSink = new KafkaSink<OrderEvent>(
//             new KafkaSinkOptions<OrderEvent> {
//                 BootstrapServers = "localhost:9092",
//                 Topic = "order-events-audit",
//                 Serializer = e => JsonSerializer.SerializeToUtf8Bytes(e),
//                 KeySelector = e => e.OrderId
//             },
//             loggerFactory.CreateLogger<KafkaSink<OrderEvent>>());
//
//         // Exactly-once sink (transactional)
//         var exactlyOnceSink = new KafkaTransactionalSink<OrderEvent>(
//             new KafkaTransactionalSinkOptions<OrderEvent> {
//                 BootstrapServers = "localhost:9092",
//                 Topic = "order-events",
//                 TransactionalId = $"order-processor-{Environment.MachineName}",
//                 Serializer = e => JsonSerializer.SerializeToUtf8Bytes(e),
//                 KeySelector = e => e.OrderId
//             },
//             loggerFactory.CreateLogger<KafkaTransactionalSink<OrderEvent>>());
//
//         // Build processor with sinks
//         await using var worker = KurrentProcessors
//             .Create<OrderEvent>(new OrderProcessor())
//             .ConnectTo("esdb://localhost:2113")
//             .WithProcessorId("order-processor")
//             .WithWorkerId(Environment.MachineName)
//             .WithLoggerFactory(loggerFactory)
//             .AddSink(atLeastOnceSink)    // Audit log - duplicates OK
//             .AddSink(exactlyOnceSink)    // Business events - exactly-once
//             .Build();
//
//         // Run until cancelled
//         using var cts = new CancellationTokenSource();
//         Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
//
//         await worker.RunAsync(cts.Token);
//     }
// }
//
// // ═══════════════════════════════════════════════════════════════════════════
// // DOMAIN TYPES
// // ═══════════════════════════════════════════════════════════════════════════
//
// public record OrderPlaced(string OrderId, decimal Total, string CustomerId);
// public record OrderEvent(string OrderId, string Status, decimal Total);
