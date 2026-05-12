// using Confluent.Kafka;
// using Microsoft.Extensions.Logging;
//
// namespace Kurrent.Streaming.Sinks.Kafka;
//
// /// <summary>
// /// Fire-and-forget Kafka sink with at-least-once delivery.
// /// <para>
// /// Records are flushed on checkpoint. May duplicate on replay.
// /// </para>
// /// </summary>
// public sealed class KafkaSink<TRecord> : ISink<TRecord> {
//     readonly KafkaSinkOptions<TRecord> _options;
//     readonly ILogger _logger;
//     readonly IProducer<string?, byte[]> _producer;
//
//     public string SinkId { get; }
//
//     public KafkaSink(KafkaSinkOptions<TRecord> options, ILogger<KafkaSink<TRecord>> logger, string? sinkId = null) {
//         _options = options;
//         _logger = logger;
//         SinkId = sinkId ?? $"kafka-{options.Topic}";
//
//         var config = new ProducerConfig {
//             BootstrapServers = options.BootstrapServers,
//             Acks = Acks.All,
//             EnableIdempotence = true
//         };
//
//         _producer = new ProducerBuilder<string?, byte[]>(config).Build();
//         _logger.LogInformation("Created Kafka sink {SinkId} for topic {Topic}", SinkId, options.Topic);
//     }
//
//     public ValueTask WriteAsync(TRecord record, CancellationToken ct = default) {
//         var message = new Message<string?, byte[]> {
//             Key = _options.KeySelector(record),
//             Value = _options.Serializer(record)
//         };
//
//         if (_options.HeadersFactory is not null)
//             message.Headers = _options.HeadersFactory(record);
//
//         _producer.Produce(_options.Topic, message);
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask FlushAsync(CancellationToken ct = default) {
//         _producer.Flush(_options.FlushTimeout);
//         _logger.LogDebug("Kafka sink {SinkId} flushed", SinkId);
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask DisposeAsync() {
//         _producer.Flush(_options.FlushTimeout);
//         _producer.Dispose();
//         return ValueTask.CompletedTask;
//     }
// }
