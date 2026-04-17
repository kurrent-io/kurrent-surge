using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Gateway;

public interface ISubscriptionSource : IAsyncDisposable {
    /// <summary>
    /// Loads the latest checkpoint, resolves the start position, initializes the
    /// position tracker, opens the subscription, and yields records and checkpoint
    /// triggers. Records are yielded with partitions already calculated. Checkpoint
    /// messages carry snapshotted positions. The source starts a periodic checkpoint
    /// timer after connection. All config comes from ProcessorOptions at construction.
    /// </summary>
    IAsyncEnumerable<SubscribeMessage> Subscribe(CancellationToken ct);

    /// <summary>
    /// Tells the source a record was successfully processed.
    /// The source tracks the position for the record's partition.
    /// </summary>
    void Track(Record record);

    /// <summary>
    /// Completes a checkpoint barrier. Persists positions to the checkpoint store
    /// and advances the position tracker. Call after the user's OnCheckpoint runs.
    /// The checkpoint ID correlates with the Checkpoint message yielded by Subscribe.
    /// If persistence fails, the tracker is not advanced — positions stay buffered
    /// for retry on the next checkpoint.
    /// </summary>
    ValueTask<PartitionPositions> CompleteCheckpointAsync(
        string checkpointId, CancellationToken ct = default);

    /// <summary>
    /// Creates a shutdown checkpoint with snapshotted positions for the final
    /// checkpoint protocol during graceful shutdown. Does not persist — call
    /// <see cref="CompleteCheckpointAsync"/> after the user's OnCheckpoint runs.
    /// </summary>
    Checkpoint CreateShutdownCheckpoint();
}
