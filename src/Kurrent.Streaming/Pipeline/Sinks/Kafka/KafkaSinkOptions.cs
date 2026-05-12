// using Confluent.Kafka;
//
// namespace Kurrent.Streaming.Sinks.Kafka;
//
// /// <summary>
// /// Configuration for Kafka sinks.
// /// </summary>
// public record KafkaSinkOptions<TRecord> {
//     public required string BootstrapServers { get; init; }
//     public required string Topic { get; init; }
//     public required Func<TRecord, byte[]> Serializer { get; init; }
//     public Func<TRecord, string?> KeySelector { get; init; } = _ => null;
//     public Func<TRecord, Headers>? HeadersFactory { get; init; }
//     public TimeSpan FlushTimeout { get; init; } = TimeSpan.FromSeconds(30);
// }
//
// /// <summary>
// /// Configuration for transactional Kafka sink.
// /// </summary>
// public record KafkaTransactionalSinkOptions<TRecord> : KafkaSinkOptions<TRecord> {
//     /// <summary>
//     /// Transactional ID - must be unique per sink and stable across restarts.
//     /// </summary>
//     public required string TransactionalId { get; init; }
//     public TimeSpan TransactionTimeout { get; init; } = TimeSpan.FromSeconds(60);
// }
