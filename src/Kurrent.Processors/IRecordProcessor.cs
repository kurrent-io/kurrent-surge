using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Processing;

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
///         if (ctx.Reason == ShutdownReason.Error)
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
    /// Called when the processor starts, before any records are processed.
    /// </summary>
    /// <param name="context">
    /// Context containing processor information, settings, and cancellation token.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when partitions are assigned to this processor.
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
    ValueTask OnPartitionsAssigned(PartitionsAssignedContext context) => ValueTask.CompletedTask;

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
    /// Called when a checkpoint barrier is received.
    /// </summary>
    ValueTask OnCheckpointBarrier(CheckpointBarrierContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when a checkpoint completes successfully.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this to commit side effects to external systems (databases, message queues)
    /// that should be coordinated with checkpoint completion.
    /// </para>
    /// <para>
    /// Notifications are best-effort and may be skipped during failures.
    /// Design external commits for idempotency.
    /// </para>
    /// </remarks>
    /// <param name="context">
    /// Context containing checkpoint information, metrics, and cancellation token.
    /// </param>
    ValueTask OnCheckpointComplete(CheckpointCompleteContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when processing stops, regardless of reason.
    /// <remarks>
    /// <para>
    /// This method is invoked at the end of the processing lifecycle, allowing for cleanup.
    /// </para>
    /// <para>
    /// Check <see cref="ShutdownContext.Reason"/> to determine why processing stopped.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the shutdown reason, final metrics, checkpoint state,
    /// error (if any), and cancellation token.
    /// </param>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// Context provided when a record processor is initialized (before Connect).
/// </summary>
/// <remarks>
/// <para>
/// Use this for setup: logging, connections, caches, configuration validation.
/// </para>
/// </remarks>
/// <param name="Settings">The processor settings used for this operation.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record InitializationContext(
    ProcessorSettings Settings,
    ILogger Logger,
    CancellationToken CancellationToken
);

[PublicAPI]
public record PartitionsAssignedContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    IStateStore State,
    ILogger Logger,
    CancellationToken CancellationToken
);

/// <summary>
/// Context provided when records are available for processing.
/// </summary>
/// <param name="ProcessorId">The processor group identifier.</param>
/// <param name="WorkerId">The worker instance identifier.</param>
/// <param name="SessionId">The server-assigned session identifier.</param>
/// <param name="Records">The batch of records to process.</param>
/// <param name="Progress">Current processing progress and metrics.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record ProcessRecordsContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    RecordBatch Records,
    ProcessorProgress Progress,
    IStateStore State,
    ILogger Logger,
    CancellationToken CancellationToken
) {
    /// <summary>
    /// Signals that processing should stop immediately.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static")]
    public void StopProcessing() => throw new OperationCanceledException("Processing stopped by user request.");
}

/// <summary>
/// Context provided when a checkpoint barrier is received.
/// </summary>
[PublicAPI]
public record CheckpointBarrierContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    ProcessorProgress Progress,
    CheckpointBarrier Barrier,
    IStateStore State,
    ILogger Logger,
    CancellationToken CancellationToken
);

/// <summary>
/// Context provided when a checkpoint completes successfully.
/// </summary>
/// <param name="ProcessorId">The processor group identifier.</param>
/// <param name="WorkerId">The worker instance identifier.</param>
/// <param name="SessionId">The server-assigned session identifier.</param>
/// <param name="Progress">Current processing progress and metrics.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record CheckpointCompleteContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    ProcessorProgress Progress,
    CheckpointBarrier Barrier,
    IStateStore State,
    ILogger Logger,
    CancellationToken CancellationToken
);

/// <summary>
/// Context provided when processing stops, regardless of reason.
/// </summary>
/// <param name="ProcessorId">The processor group identifier.</param>
/// <param name="WorkerId">The worker instance identifier.</param>
/// <param name="SessionId">The server-assigned session identifier, or <see cref="SessionId.None"/> if never connected.</param>
/// <param name="Progress">Final processing progress and metrics.</param>
/// <param name="Error">The error that caused shutdown, or null if not an error.</param>
[PublicAPI]
public record ShutdownContext(
    ProcessorId ProcessorId,
    WorkerId WorkerId,
    SessionId SessionId,
    ProcessorProgress Progress,
    ILogger Logger,
    Exception? Error = null
) {
    public bool IsError => Error is not null;
}

/// <summary>
/// Combined progress information for a processor session.
/// </summary>
/// <param name="Throughput">Throughput and timing metrics.</param>
/// <param name="Checkpoint">Checkpoint progress information.</param>
/// <param name="Partitions">Per-partition position progress.</param>
[PublicAPI]
public readonly record struct ProcessorProgress(
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
    public static readonly ProcessorProgress Empty = new(
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
/// <param name="LastBarrierId">Barrier ID of the last completed checkpoint.</param>
/// <param name="LastTrigger">What triggered the last checkpoint.</param>
/// <param name="LastCompletedAt">When the last checkpoint completed, or null if none.</param>
[PublicAPI]
public readonly record struct CheckpointProgress(
    long TotalCheckpoints,
    long RecordsSinceLastCheckpoint,
    BarrierId LastBarrierId,
    CheckpointBarrierTrigger LastTrigger,
    DateTimeOffset? LastCompletedAt
) {
    /// <summary>
    /// Empty checkpoint progress with no checkpoints completed.
    /// </summary>
    public static readonly CheckpointProgress Empty = new(0, 0, BarrierId.None, CheckpointBarrierTrigger.Unspecified, null);
}

/// <summary>
/// Why processing stopped.
/// </summary>
[PublicAPI]
public enum ShutdownReason {
    /// <summary>
    /// Unspecified reason. For proto compatibility.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// Processing completed successfully (reached end of stream if bounded).
    /// </summary>
    Completed = 1,

    /// <summary>
    /// Processing was cancelled either by user request or server-side timeout.
    /// </summary>
    Cancelled = 2,

    /// <summary>
    /// An error occurred during processing.
    /// </summary>
    Error = 3
}
