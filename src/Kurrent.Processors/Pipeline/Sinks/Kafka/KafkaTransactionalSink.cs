// using System.Text.Json;
// using Confluent.Kafka;
// using Microsoft.Extensions.Logging;
//
// namespace Kurrent.Processors.Sinks.Kafka;
//
// /// <summary>
// /// Transactional Kafka sink with exactly-once delivery.
// /// <para>
// /// Uses Kafka transactions. The runtime coordinates:
// /// <list type="number">
// ///   <item>Writes buffered in transaction</item>
// ///   <item>On barrier: <see cref="PrepareCheckpointAsync"/> flushes, returns committable</item>
// ///   <item>After checkpoint: <see cref="CommitAsync"/> commits transaction</item>
// /// </list>
// /// </para>
// /// </summary>
// public sealed class KafkaTransactionalSink<TRecord> : ITransactionalSink<TRecord> {
//     readonly KafkaTransactionalSinkOptions<TRecord> _options;
//     readonly ILogger _logger;
//     readonly IProducer<string?, byte[]> _producer;
//
//     bool _initialized;
//     bool _transactionActive;
//     int _messagesInTransaction;
//
//     public string SinkId { get; }
//
//     public KafkaTransactionalSink(
//         KafkaTransactionalSinkOptions<TRecord> options,
//         ILogger<KafkaTransactionalSink<TRecord>> logger,
//         string? sinkId = null) {
//         _options = options;
//         _logger = logger;
//         SinkId = sinkId ?? $"kafka-txn-{options.Topic}";
//
//         var config = new ProducerConfig {
//             BootstrapServers = options.BootstrapServers,
//             TransactionalId = options.TransactionalId,
//             TransactionTimeoutMs = (int)options.TransactionTimeout.TotalMilliseconds,
//             Acks = Acks.All,
//             EnableIdempotence = true
//         };
//
//         _producer = new ProducerBuilder<string?, byte[]>(config).Build();
//         _logger.LogInformation(
//             "Created transactional Kafka sink {SinkId} for topic {Topic}",
//             SinkId, options.Topic);
//     }
//
//     void EnsureInitialized() {
//         if (_initialized) return;
//         _producer.InitTransactions(_options.TransactionTimeout);
//         _initialized = true;
//     }
//
//     void EnsureTransactionActive() {
//         if (_transactionActive) return;
//         EnsureInitialized();
//         _producer.BeginTransaction();
//         _transactionActive = true;
//         _messagesInTransaction = 0;
//     }
//
//     public async ValueTask WriteAsync(TRecord record, CancellationToken ct = default) {
//         EnsureTransactionActive();
//
//         var message = new Message<string?, byte[]> {
//             Key = _options.KeySelector(record),
//             Value = _options.Serializer(record)
//         };
//
//         if (_options.HeadersFactory is not null)
//             message.Headers = _options.HeadersFactory(record);
//
//         await _producer.ProduceAsync(_options.Topic, message, ct);
//         _messagesInTransaction++;
//     }
//
//     public ValueTask FlushAsync(CancellationToken ct = default) {
//         _producer.Flush(_options.FlushTimeout);
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask<SinkCheckpointState?> PrepareCheckpointAsync(CancellationToken ct = default) {
//         if (!_transactionActive || _messagesInTransaction == 0)
//             return ValueTask.FromResult<SinkCheckpointState?>(null);
//
//         _producer.Flush(_options.FlushTimeout);
//
//         var payload = new KafkaCommittablePayload(_options.TransactionalId, _messagesInTransaction);
//
//         _logger.LogDebug(
//             "Kafka sink {SinkId} prepared: {Count} messages",
//             SinkId, _messagesInTransaction);
//
//         return ValueTask.FromResult<SinkCheckpointState?>(
//             new SinkCheckpointState(SinkId, JsonSerializer.SerializeToUtf8Bytes(payload)));
//     }
//
//     public ValueTask CommitAsync(SinkCheckpointState checkpointState, CancellationToken ct = default) {
//         if (!_transactionActive) {
//             _logger.LogDebug("No active transaction for {SinkId}, skipping commit", SinkId);
//             return ValueTask.CompletedTask;
//         }
//
//         _producer.CommitTransaction();
//         _transactionActive = false;
//         _messagesInTransaction = 0;
//
//         _logger.LogDebug("Kafka sink {SinkId} committed", SinkId);
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask AbortAsync(CancellationToken ct = default) {
//         if (!_transactionActive) return ValueTask.CompletedTask;
//
//         _producer.AbortTransaction();
//         _transactionActive = false;
//         _messagesInTransaction = 0;
//
//         _logger.LogDebug("Kafka sink {SinkId} aborted", SinkId);
//         return ValueTask.CompletedTask;
//     }
//
//     public async ValueTask DisposeAsync() {
//         if (_transactionActive)
//             await AbortAsync();
//         _producer.Dispose();
//     }
//
//     record KafkaCommittablePayload(string TransactionalId, int MessageCount);
// }
