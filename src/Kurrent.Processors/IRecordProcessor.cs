using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Processors;

/// <summary>
/// Defines the interface for processing records from a processor.
/// Implement this interface to receive callbacks during the processing lifecycle.
/// <para>
/// All methods except <see cref="ProcessRecord"/> have default implementations
/// that do nothing, allowing you to override only the methods you need.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// All lifecycle methods are informational. The SDK handles checkpointing,
/// state management, and coordination automatically.
/// </para>
/// </remarks>
[PublicAPI]
public interface IRecordProcessor {
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

    /// <summary>
    /// Called when a checkpoint is signaled to allow for pre-checkpoint actions like flushing state.
    /// </summary>
    ValueTask OnCheckpoint(CheckpointContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when the worker encounters an unrecoverable error.
    /// <remarks>
    /// <para>
    /// This method is invoked before the worker transitions to <see cref="ProcessorWorkerStatus.Error"/>.
    /// Use it for error reporting, metrics, or cleanup.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the error and final progress snapshot.
    /// </param>
    ValueTask OnError(ErrorContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when the worker completes gracefully (including cancellation).
    /// <remarks>
    /// <para>
    /// This method is invoked before the worker transitions to <see cref="ProcessorWorkerStatus.Stopped"/>.
    /// Use it for final reporting or cleanup.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the final progress snapshot.
    /// </param>
    ValueTask OnComplete(CompleteContext context) => ValueTask.CompletedTask;
}

public record WorkerContext {
    public static readonly WorkerContext None = new WorkerContext();

    public ProcessorId       ProcessorId       { get; init; } = ProcessorId.None;
    public WorkerId          WorkerId          { get; init; } = WorkerId.None;
    public SessionId         SessionId         { get; init; } = SessionId.None;
    public ILogger           Logger            { get; init; } = NullLogger.Instance;
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    public ProcessRecordContext ForProcessRecord(Record record, WorkerProgress progress) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Record            = record,
            Progress          = progress
        };

    public CheckpointContext ForCheckpoint(Checkpoint checkpoint) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            CheckpointId      = checkpoint.CheckpointId,
            Trigger           = checkpoint.Trigger,
            Timestamp         = checkpoint.Timestamp
        };

    public ErrorContext ForError(WorkerProgress progress, Exception error) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Progress          = progress,
            Error             = error
        };

    public CompleteContext ForComplete(WorkerProgress progress) =>
        new() {
            ProcessorId       = ProcessorId,
            WorkerId          = WorkerId,
            SessionId         = SessionId,
            Logger            = Logger,
            CancellationToken = CancellationToken,
            Progress          = progress
        };
}

/// <summary>
/// Context provided when a record is available for processing.
/// </summary>
[PublicAPI]
public record ProcessRecordContext : WorkerContext {
    public required Record         Record   { get; init; }
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
    public string            CheckpointId { get; init; } = string.Empty;
    public CheckpointTrigger Trigger      { get; init; }
    public DateTimeOffset    Timestamp    { get; init; }
}

/// <summary>
/// Context provided when the worker encounters an unrecoverable error.
/// </summary>
[PublicAPI]
public record ErrorContext : WorkerContext {
    public required WorkerProgress Progress { get; init; }
    public required Exception      Error    { get; init; }
}

/// <summary>
/// Context provided when the worker completes gracefully.
/// </summary>
[PublicAPI]
public record CompleteContext : WorkerContext {
    public required WorkerProgress Progress { get; init; }
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
