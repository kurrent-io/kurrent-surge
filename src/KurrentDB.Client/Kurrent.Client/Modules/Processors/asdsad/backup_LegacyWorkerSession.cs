// using System.Collections.Immutable;
// using System.Threading.Channels;
// using Kurrent.Client.Processors.Checkpointing;
// using Kurrent.Client.Processors.Messages;
// using Kurrent.Client.Streams.Processing;
//
// namespace Kurrent.Client.Streams;
//
// record LegacyWorkerSession : IWorkerSession {
//     internal LegacyWorkerSession(Guid sessionId, Channel<LegacyGatewayMessage> channel, BatchingSettings settings) {
//         SessionId = sessionId;
//         Channel   = channel;
//         Settings  = settings;
//     }
//
//     Channel<LegacyGatewayMessage> Channel  { get; }
//     BatchingSettings              Settings { get; }
//
//     public Guid SessionId { get; }
//
//     public IAsyncEnumerator<ProcessorControlMessage> GetAsyncEnumerator(CancellationToken ct = new CancellationToken()) {
//         var messages = Channel.Reader.ReadAllAsync(ct).ReadBatched(
//             batchSize: Settings.MaxSize,
//             timeout: TimeSpan.FromMilliseconds(Settings.Timeout),
//             classify: static msg => msg.IsRecord
//                 ? BatchAction<Record, ProcessorControlMessage>.Batch(msg.AsRecord)
//                 : BatchAction<Record, ProcessorControlMessage>.FlushThenYield(msg.AsCheckpointBarrier),
//             batchToOutput: static records => RecordBatch.From([..records]),
//             cancellationToken: ct);
//
//         return messages.GetAsyncEnumerator(ct);
//     }
//
//     public PartitionedPositionTracker Tracker { get; private set; } = new(ImmutableDictionary<Partition, LogPosition>.Empty, LogPosition.Unset);
//
//     public void ResetPositionTracking(LogPosition lastBarrierId, ImmutableDictionary<Partition, LogPosition> positions) =>
//         Tracker = new PartitionedPositionTracker(positions, lastBarrierId);
// }
