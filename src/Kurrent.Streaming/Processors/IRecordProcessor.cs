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

    public BatchContext ForBatch(DroneProgress progress, RecordBatch records, Action<RecordPosition> requestTrack) =>
        new(requestTrack) {
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

    public CheckpointContext ForCheckpoint(
        Checkpoint checkpoint,
        RecordPosition[] positions,
        RecordPosition? scanPosition,
        Action<RecordPosition> requestTrack
    ) =>
        new(requestTrack) {
            ProcessorId       = ProcessorId,
            DroneId           = DroneId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Positions         = positions,
            CheckpointId      = checkpoint.Id,
            Trigger           = checkpoint.TriggerKind,
            Timestamp         = checkpoint.Timestamp,
            Reason            = (checkpoint as ManualCheckpoint)?.Reason,
            ScanPosition      = scanPosition
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
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(Record record) {
        _trackedByUser = true;
        RequestTrack(record.Position);
    }

    /// <summary>
    /// Tells the framework that the given position has been durably handled.
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
    /// runs on top, but the tracker is idempotent.
    /// </summary>
    public void Track(RecordPosition position) {
        _trackedByUser = true;
        RequestTrack(position);
    }

    /// <summary>
    /// Tells the framework that the given positions have been durably handled.
    /// Always valid; in <c>AutoTrack = true</c> mode the framework's auto-track
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
    /// A checkpoint has already been requested for this record, or a stop has already been requested
    /// (after <see cref="StopProcessing"/> the shutdown barrier is the single commit point).
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Cancellation has been requested on the context's token.
    /// </exception>
    public void Checkpoint(string? reason = null) {
        if (_checkpointRequested)
            throw new InvalidOperationException("Checkpoint already requested for this record");

        // Once a stop is requested the graceful Shutdown barrier becomes the single OnCheckpoint +
        // commit point, so a manual checkpoint is meaningless — reject it rather than queue a barrier
        // the drone will suppress.
        if (_stopRequested)
            throw new InvalidOperationException("Cannot request a checkpoint after StopProcessing.");

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
/// Context provided when a checkpoint is signaled. Fires on every barrier (interval, manual, server
/// scan watermark, shutdown) and runs <b>before</b> the commit snapshot, so positions tracked here
/// via <see cref="Track(RecordPosition)"/> land in this barrier's commit.
/// </summary>
[PublicAPI]
public record CheckpointContext : DroneContext {
    public CheckpointContext() { }

    public CheckpointContext(Action<RecordPosition> requestTrack) =>
        RequestTrack = requestTrack;

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

    /// <summary>
    /// The server scan watermark for a <see cref="CheckpointTrigger.Filter"/> or
    /// <see cref="CheckpointTrigger.CaughtUp"/> barrier — the log position the shared scan has
    /// reached over regions with no matching records. The barrier carries a log position and has no
    /// knowledge of partitions; <see cref="Track(RecordPosition)"/> folds it across this drone's
    /// owned partitions. <c>null</c> for every other trigger.
    /// </summary>
    /// <remarks>
    /// With <c>AutoTrack = true</c> the framework already folds this watermark across owned
    /// partitions, so the value is informational. With <c>AutoTrack = false</c> the framework
    /// advances nothing — call <c>ctx.Track(ctx.ScanPosition!)</c> to claim the scanned span, or a
    /// cold restart rescans the already-scanned no-match / caught-up region.
    /// </remarks>
    public RecordPosition? ScanPosition { get; init; }

    internal Action<RecordPosition> RequestTrack { get; init; } = static _ => { };

    /// <summary>
    /// Marks the given position's log position as durably handled, from inside <c>OnCheckpoint</c>,
    /// folding it across this drone's owned partitions. Because the callback runs before the commit
    /// snapshot, the position is committed by this same barrier. Positions at or below the current
    /// per-partition progress are skipped, so re-tracking a stale watermark is a no-op.
    /// </summary>
    /// <param name="position">The position to track — typically <see cref="ScanPosition"/>.</param>
    public void Track(RecordPosition position) =>
        RequestTrack(position);

    /// <summary>
    /// Marks each of the given positions as durably handled, from inside <c>OnCheckpoint</c>,
    /// folding each across this drone's owned partitions.
    /// </summary>
    /// <param name="positions">The positions to track.</param>
    public void Track(IEnumerable<RecordPosition> positions) {
        foreach (var position in positions)
            RequestTrack(position);
    }
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
    IReadOnlyList<PartitionProgress> Partitions
) {
    /// <summary>
    /// Number of partitions currently assigned.
    /// </summary>
    public int PartitionCount => Partitions.Count;

    /// <summary>
    /// Records tracked but not yet committed — the reprocess-on-crash window, in records.
    /// Equivalent to <see cref="CheckpointProgress.RecordsSinceLastCheckpoint"/>.
    /// </summary>
    public long UncommittedRecords => Checkpoint.RecordsSinceLastCheckpoint;

    /// <summary>
    /// Bytes tracked but not yet committed across all partitions — the reprocess-on-crash window,
    /// in bytes. The sum of each partition's <see cref="PartitionProgress.UncommittedBytes"/>.
    /// </summary>
    public long UncommittedBytes {
        get {
            var total = 0L;
            foreach (var partition in Partitions)
                total += partition.UncommittedBytes;
            return total;
        }
    }

    /// <summary>
    /// Gets progress for a specific partition. Entries are self-describing, so this scans the list.
    /// </summary>
    /// <param name="partition">The partition to get progress for.</param>
    /// <returns>The partition's progress.</returns>
    /// <exception cref="KeyNotFoundException">If the partition is not being tracked.</exception>
    public PartitionProgress this[Partition partition] {
        get {
            foreach (var p in Partitions)
                if (p.Partition == partition)
                    return p;

            throw new KeyNotFoundException($"Partition {partition} is not being tracked.");
        }
    }

    /// <summary>
    /// Tries to get progress for a specific partition.
    /// </summary>
    /// <param name="partition">The partition to get progress for.</param>
    /// <param name="progress">The partition's progress if found.</param>
    /// <returns>True if the partition was found; otherwise false.</returns>
    public bool TryGetPartition(Partition partition, out PartitionProgress progress) {
        foreach (var p in Partitions)
            if (p.Partition == partition) {
                progress = p;
                return true;
            }

        progress = PartitionProgress.Empty;
        return false;
    }

    /// <summary>
    /// Gets the partition with the lowest checkpoint position.
    /// Useful for monitoring which partition is furthest behind.
    /// </summary>
    /// <returns>The partition and its progress, or null if no partitions are assigned.</returns>
    public (Partition Partition, PartitionProgress Progress)? LowestCheckpointPartition {
        get {
            if (Partitions.Count == 0)
                return null;

            var min = Partitions.MinBy(x => x.LastFlushedPosition);
            return (min.Partition, min);
        }
    }

    /// <summary>
    /// Empty progress with no records processed and no checkpoints.
    /// </summary>
    public static readonly DroneProgress Empty = new(
        ThroughputMetrics.Empty,
        CheckpointProgress.Empty,
        []
    );
}

/// <summary>
/// Position progress for a single partition. Self-describing — carries its own <see cref="Partition"/>,
/// so a snapshot holds these in a flat list rather than a dictionary keyed by partition.
/// </summary>
/// <param name="Partition">The partition this progress belongs to.</param>
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
    Partition      Partition,
    RecordPosition LastTrackedPosition,
    RecordPosition LastFlushedPosition
) {
    /// <summary>
    /// Empty partition progress with no positions tracked or flushed.
    /// </summary>
    public static readonly PartitionProgress Empty = new(default, RecordPosition.Unset, RecordPosition.Unset);

    /// <summary>
    /// Bytes tracked but not yet committed for this partition — the replay window. The gap between
    /// the last tracked and last flushed log position (log positions are byte offsets). Zero when
    /// nothing is pending or no checkpoint has flushed yet.
    /// </summary>
    public long UncommittedBytes {
        get {
            var tracked = LastTrackedPosition.LogPosition;
            var flushed = LastFlushedPosition.LogPosition;

            return tracked != LogPosition.Unset && flushed != LogPosition.Unset && tracked > flushed
                ? tracked.Value - flushed.Value
                : 0;
        }
    }
}

/// <summary>
/// Throughput and timing metrics for a processor session.
/// </summary>
/// <param name="TotalRecords">Total number of records processed.</param>
/// <param name="RecordsPerSecond">Average records processed per second.</param>
/// <param name="StartedAt">When processing started.</param>
/// <param name="Elapsed">Total time spent processing.</param>
/// <param name="LastProcessedAt">Event-time of the last dispatched record, or <c>null</c> if none processed yet.</param>
[PublicAPI]
public readonly record struct ThroughputMetrics(
    long TotalRecords,
    double RecordsPerSecond,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed,
    DateTime? LastProcessedAt = null
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
