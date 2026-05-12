using System.Collections.Immutable;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Models;

namespace Kurrent.Streaming;

/// <summary>
/// Defines the interface for processing records one at a time.
/// Implement this interface to receive callbacks during the processing lifecycle.
/// <para>
/// Lifecycle methods (<see cref="IProcessor.OnConnected"/>, <see cref="IProcessor.OnCheckpoint"/>)
/// are inherited from <see cref="IProcessor"/> with default no-op implementations. Only
/// <see cref="ProcessRecord"/> must be implemented.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// All lifecycle methods are informational. The SDK handles checkpointing,
/// state management, and coordination automatically.
/// </para>
/// <para>
/// For batched dispatch, see <see cref="IRecordBatchProcessor"/>.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRecordProcessor : IProcessor {
    /// <summary>
    /// Called when a record is available for processing.
    /// <remarks>
    /// This is the only method that must be implemented.
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the record and cancellation token.
    /// </param>
    ValueTask ProcessRecord(ProcessRecordContext context);
}

public record DroneContext {
    public static readonly DroneContext None = new DroneContext();

    public ProcessorId ProcessorId { get; init; } = ProcessorId.Unset;

    public string DroneId { get; init; } = "";

    public SessionId SessionId { get; init; } = SessionId.Unset;

    public ILogger Logger { get; init; } = NullLogger.Instance;

    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    public ProcessRecordContext ForProcessRecord(DroneProgress progress, Record record, Action<RecordPosition> requestTrack) =>
        new(requestTrack) {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Record            = record,
            Progress          = progress
        };

    public BatchContext ForBatch(DroneProgress progress, Kurrent.Client.Processors.Gateway.Messages.RecordBatch records) =>
        new() {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Records           = records,
            Progress          = progress
        };

    public ConnectedContext ForConnected() =>
        new() {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken
        };

    public CheckpointContext ForCheckpoint(RecordPosition[] positions, Checkpoint checkpoint) =>
        new() {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Positions         = positions,
            CheckpointId      = checkpoint.CheckpointId,
            Trigger           = checkpoint.Trigger,
            Timestamp         = checkpoint.Timestamp,
            Reason            = checkpoint.Reason
        };

}

/// <summary>
/// Context provided when a record is available for processing.
/// </summary>
[PublicAPI]
public record ProcessRecordContext : DroneContext {

    public ProcessRecordContext() { }

    public ProcessRecordContext(Action<RecordPosition> requestTrack) =>
        RequestTrack = requestTrack;

    public required Record        Record   { get; init; }
    public required DroneProgress Progress { get; init; }

    public Action<RecordPosition> RequestTrack { get; init; } = _ => { };

    /// <summary>
    /// Tells the framework that the given record has been durably handled.
    /// Always valid; in <c>AutoCheckpoint = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(Record record) {
        _trackedByUser = true;
        RequestTrack(record.Position);
    }

    /// <summary>
    /// Tells the framework that the given position has been durably handled.
    /// Always valid; in <c>AutoCheckpoint = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(RecordPosition position) {
        _trackedByUser = true;
        RequestTrack(position);
    }

    /// <summary>
    /// Tells the framework that the given positions have been durably handled.
    /// Always valid; in <c>AutoCheckpoint = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(IEnumerable<RecordPosition> positions) {
        foreach (var p in positions) {
            _trackedByUser = true;
            RequestTrack(p);
        }
    }

    /// <summary>
    /// Requests that processing stop after this record. The framework will inject a
    /// shutdown marker once the current dispatch finishes. This record will NOT be
    /// auto-tracked — call <see cref="Track(Record)"/> first if you want it claimed.
    /// </summary>
    public void StopProcessing(string? reason = null) {
        _stopRequested = true;
        _stopReason    = reason;
    }

    /// <summary>
    /// Requests a manual checkpoint barrier. The framework will raise
    /// <c>OnCheckpoint</c> with <see cref="CheckpointTrigger.Manual"/> and the supplied
    /// <paramref name="reason"/>; positions are taken from the tracker's state at the
    /// moment the framework dispatches the marker (after auto-track for this record).
    /// At most one manual checkpoint may be requested per <c>ProcessRecord</c> invocation.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A checkpoint has already been requested for this record.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation has been requested on the context's token.
    /// </exception>
    public void Checkpoint(string? reason = null) {
        if (_checkpointRequested)
            throw new InvalidOperationException("Checkpoint already requested for this record");

        CancellationToken.ThrowIfCancellationRequested();

        _checkpointRequested = true;
        _checkpointReason    = reason;
    }

    bool    _checkpointRequested;
    string? _checkpointReason;

    bool    _stopRequested;
    string? _stopReason;

    bool    _trackedByUser;

    internal bool    CheckpointRequested => _checkpointRequested;
    internal string? CheckpointReason    => _checkpointReason;

    internal bool    StopRequested  => _stopRequested;
    internal string? StopReason     => _stopReason;
    internal bool    TrackedByUser  => _trackedByUser;
}

/// <summary>
/// Context provided when the drone has connected and is ready to process records.
/// </summary>
[PublicAPI]
public record ConnectedContext : DroneContext;

/// <summary>
/// Context provided when a checkpoint is signaled.
/// </summary>
[PublicAPI]
public record CheckpointContext : DroneContext {
    public required RecordPosition[]   Positions    { get; init; }
    public required string             CheckpointId { get; init; }
    public required CheckpointTrigger  Trigger      { get; init; }
    public required DateTimeOffset     Timestamp    { get; init; }

    /// <summary>
    /// Optional reason associated with this checkpoint. Populated when
    /// <see cref="Trigger"/> is <see cref="CheckpointTrigger.Manual"/> and the user
    /// passed a reason to <c>ctx.Checkpoint(reason)</c>. Null otherwise.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Combined progress information for a processor session.
/// </summary>
/// <param name="Throughput">Throughput and timing metrics.</param>
/// <param name="Checkpoint">Checkpoint progress information.</param>
/// <param name="Partitions">Per-partition position progress.</param>
[PublicAPI]
public readonly record struct DroneProgress(
    ThroughputMetrics Throughput,
    CheckpointProgress Checkpoint,
    ImmutableDictionary<Partition, PartitionProgress> Partitions
) {
    /// <summary>
    /// Number of partitions currently assigned.
    /// </summary>
    public int PartitionCount => Partitions.Count;

    /// <summary>
    /// Gets progress for a specific partition.
    /// </summary>
    /// <param name="partition">The partition to get progress for.</param>
    /// <returns>The partition's progress.</returns>
    /// <exception cref="KeyNotFoundException">If the partition is not being tracked.</exception>
    public PartitionProgress this[Partition partition] => Partitions[partition];

    /// <summary>
    /// Tries to get progress for a specific partition.
    /// </summary>
    /// <param name="partition">The partition to get progress for.</param>
    /// <param name="progress">The partition's progress if found.</param>
    /// <returns>True if the partition was found; otherwise false.</returns>
    public bool TryGetPartition(Partition partition, out PartitionProgress progress) =>
        Partitions.TryGetValue(partition, out progress);

    /// <summary>
    /// Gets the partition with the lowest checkpoint position.
    /// Useful for monitoring which partition is furthest behind.
    /// </summary>
    /// <returns>The partition and its progress, or null if no partitions are assigned.</returns>
    public (Partition Partition, PartitionProgress Progress)? LowestCheckpointPartition {
        get {
            if (Partitions.Count == 0)
                return null;

            var min = Partitions.MinBy(x => x.Value.LastFlushedPosition);
            return (min.Key, min.Value);
        }
    }

    /// <summary>
    /// Empty progress with no records processed and no checkpoints.
    /// </summary>
    public static readonly DroneProgress Empty = new(
        ThroughputMetrics.Empty,
        CheckpointProgress.Empty,
        ImmutableDictionary<Partition, PartitionProgress>.Empty
    );
}

/// <summary>
/// Position progress for a single partition.
/// </summary>
/// <param name="LastTrackedPosition">
/// The most recent position tracked (processed) but not yet checkpointed.
/// Returns <see cref="LogPosition.Unset"/> if no positions are pending.
/// </param>
/// <param name="LastFlushedPosition">
/// The most recent position that has been checkpointed (flushed).
/// Returns <see cref="LogPosition.Unset"/> if no checkpoints have occurred.
/// </param>
[PublicAPI]
public readonly record struct PartitionProgress(
    RecordPosition LastTrackedPosition,
    RecordPosition LastFlushedPosition
) {
    /// <summary>
    /// Empty partition progress with no positions tracked or flushed.
    /// </summary>
    public static readonly PartitionProgress Empty = new(RecordPosition.Unset, RecordPosition.Unset);
}

/// <summary>
/// Throughput and timing metrics for a processor session.
/// </summary>
/// <param name="TotalRecords">Total number of records processed.</param>
/// <param name="RecordsPerSecond">Average records processed per second.</param>
/// <param name="StartedAt">When processing started.</param>
/// <param name="Elapsed">Total time spent processing.</param>
[PublicAPI]
public readonly record struct ThroughputMetrics(
    long TotalRecords,
    double RecordsPerSecond,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed
) {
    /// <summary>
    /// Empty metrics with no records processed.
    /// </summary>
    public static readonly ThroughputMetrics Empty = new(0, 0, DateTimeOffset.MinValue, TimeSpan.Zero);
}

/// <summary>
/// Checkpoint progress information for a processor session.
/// </summary>
/// <param name="TotalCheckpoints">Total number of checkpoints completed.</param>
/// <param name="RecordsSinceLastCheckpoint">Records processed since the last checkpoint.</param>
/// <param name="LastTrigger">What triggered the last checkpoint.</param>
/// <param name="LastCheckpointAt">When the last checkpoint completed, or null if none.</param>
[PublicAPI]
public readonly record struct CheckpointProgress(
    long TotalCheckpoints,
    long RecordsSinceLastCheckpoint,
    CheckpointTrigger LastTrigger,
    DateTimeOffset? LastCheckpointAt
) {
    /// <summary>
    /// Empty checkpoint progress with no checkpoints completed.
    /// </summary>
    public static readonly CheckpointProgress Empty = new(0, 0, CheckpointTrigger.Unspecified, null);
}
