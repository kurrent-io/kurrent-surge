using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Processors;

/// <summary>
/// Defines the interface for processing records from a processor.
/// Implement this interface to receive callbacks during the processing lifecycle.
/// <para>
/// All methods except <see cref="ProcessRecords"/> have default implementations
/// that do nothing, allowing you to override only the methods you need.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// All lifecycle methods are informational. The SDK handles checkpointing,
/// state management, and coordination automatically.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Minimal implementation - only ProcessRecords is required
/// class MinimalProcessor : IRecordProcessor {
///     public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
///         foreach (var record in ctx.Records)
///             Console.WriteLine(record.Schema.Name);
///         return ValueTask.CompletedTask;
///     }
/// }
///
/// // Full implementation with lifecycle handling
/// class FullProcessor : IRecordProcessor {
///     public ValueTask OnInitialize(InitializationContext ctx) {
///         Console.WriteLine($"Starting processor: {ctx.Settings.ProcessorName}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
///         Console.WriteLine($"Processing {ctx.Records.Count} records, total: {ctx.Progress.Throughput.TotalRecords}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask OnCheckpointComplete(CheckpointCompleteContext ctx) {
///         Console.WriteLine($"Checkpoint #{ctx.Progress.Checkpoint.TotalCheckpoints} at {ctx.Progress.Checkpoint.LastPosition}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask OnShutdown(ShutdownContext ctx) {
///         if (ctx.IsError)
///             Console.WriteLine($"Error: {ctx.Error}");
///         else
///             Console.WriteLine($"Done: {ctx.Progress.Throughput.TotalRecords} records in {ctx.Progress.Throughput.Elapsed}");
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
[PublicAPI]
public interface IRecordProcessor {
    /// <summary>
    /// Called when partitions are assigned to this processor, before any records are processed.
    /// <remarks>
    /// <para>
    /// This method is invoked whenever the set of partitions assigned to this processor changes,
    /// such as during initial assignment or rebalancing events.
    /// </para>
    /// <para>
    /// Use this method to perform any setup or state initialization required for the newly assigned partitions.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing processor information, assigned partitions, and cancellation token.
    /// </param>
    /// <returns>
    /// A <see cref="ValueTask"/> representing the asynchronous operation.
    /// </returns>
    ValueTask OnPartitionAssignment(PartitionAssignmentContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when records are available for processing.
    /// <remarks>
    /// This is the only method that must be implemented.
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the batch of records and cancellation token.
    /// </param>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>
    /// Called when a checkpoint is signaled to allow for pre-checkpoint actions like flushing state.
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

  /// <summary>
  /// Called when a checkpoint will be committed to allow for post-checkpoint actions, like 2PC finalization.
  /// </summary>
    ValueTask OnCheckpointCommit(CheckpointCommitContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when the worker is shutting down.
    /// <remarks>
    /// <para>
    /// This method is invoked at the end of the processing lifecycle, allowing for cleanup.
    /// </para>
    /// <para>
    /// Check <see cref="ShutdownContext.ErrorOccurred"/> to determine why processing stopped.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the shutdown reason, final metrics, checkpoint state,
    /// error (if any), and cancellation token.
    /// </param>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}

public record WorkerContext {
    public static readonly WorkerContext None = new WorkerContext();

    public ProcessorId       ProcessorId       { get; init; } = ProcessorId.None;
    public WorkerId          WorkerId          { get; init; } = WorkerId.None;
    public SessionId         SessionId         { get; init; } = SessionId.None;
    public ILogger           Logger            { get; init; } = NullLogger.Instance;
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    public PartitionAssignmentContext ForPartitionAssignment(Partition[] assigned, Partition[] revoked, Partition[] retained) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Assigned          = assigned,
            Revoked           = revoked,
            Retained          = retained
        };

    public ProcessRecordsContext ForProcessRecords(RecordBatch records, WorkerProgress progress) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Records           = records,
            Progress          = progress
        };

    public CheckpointContext ForCheckpoint(WorkerProgress progress, CheckpointBarrier barrier) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Progress          = progress,
            Barrier           = barrier
        };

    public CheckpointCommitContext ForCheckpointCommit(WorkerProgress progress, CheckpointBarrier barrier) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Progress          = progress,
            Barrier           = barrier
        };

    public ShutdownContext ForShutdown(WorkerProgress progress, Exception? error = null) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Progress          = progress,
            Error             = error
        };
}

[PublicAPI]
public record PartitionAssignmentContext : WorkerContext {
    public required Partition[] Assigned { get; init; }
    public required Partition[] Revoked  { get; init; }
    public required Partition[] Retained { get; init; }

    public void Deconstruct(out Partition[] assigned, out Partition[] revoked, out Partition[] retained) {
        assigned = Assigned;
        revoked  = Revoked;
        retained = Retained;
    }
}

/// <summary>
/// Context provided when records are available for processing.
/// </summary>
[PublicAPI]
public record ProcessRecordsContext : WorkerContext {
    public required RecordBatch    Records  { get; init; }
    public required WorkerProgress Progress { get; init; }

    /// <summary>
    /// Signals that processing should stop immediately.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public void StopProcessing() => throw new OperationCanceledException("Processing stopped by user request.");
}

/// <summary>
/// Context provided when a checkpoint is signaled.
/// </summary>
[PublicAPI]
public record CheckpointContext : WorkerContext {
    public WorkerProgress    Progress { get; init; }
    public CheckpointBarrier Barrier  { get; init; }
}

[PublicAPI]
public record CheckpointCommitContext : WorkerContext {
    public WorkerProgress    Progress { get; init; }
    public CheckpointBarrier Barrier  { get; init; }
}

[PublicAPI]
public record ShutdownContext : WorkerContext {
    public WorkerProgress Progress { get; init; }
    public Exception?     Error    { get; init; }

    public bool ErrorOccurred => Error is not null;
}

/// <summary>
/// Combined progress information for a processor session.
/// </summary>
/// <param name="Throughput">Throughput and timing metrics.</param>
/// <param name="Checkpoint">Checkpoint progress information.</param>
/// <param name="Partitions">Per-partition position progress.</param>
[PublicAPI]
public readonly record struct WorkerProgress(
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
    public static readonly WorkerProgress Empty = new(
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
    LogPosition LastTrackedPosition,
    LogPosition LastFlushedPosition
) {
    /// <summary>
    /// Empty partition progress with no positions tracked or flushed.
    /// </summary>
    public static readonly PartitionProgress Empty = new(LogPosition.Unset, LogPosition.Unset);
}

/// <summary>
/// Throughput and timing metrics for a processor session.
/// </summary>
/// <param name="TotalRecords">Total number of records processed.</param>
/// <param name="TotalBatches">Total number of batches processed.</param>
/// <param name="RecordsPerSecond">Average records processed per second.</param>
/// <param name="AverageBatchSize">Average number of records per batch.</param>
/// <param name="StartedAt">When processing started.</param>
/// <param name="Elapsed">Total time spent processing.</param>
[PublicAPI]
public readonly record struct ThroughputMetrics(
    long TotalRecords,
    long TotalBatches,
    double RecordsPerSecond,
    double AverageBatchSize,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed
) {
    /// <summary>
    /// Empty metrics with no records processed.
    /// </summary>
    public static readonly ThroughputMetrics Empty = new(0, 0, 0, 0, DateTimeOffset.MinValue, TimeSpan.Zero);
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
