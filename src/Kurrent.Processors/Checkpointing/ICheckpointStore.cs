using System.Collections.Immutable;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Checkpointing;

public interface ICheckpointStore {
    /// <summary>
    /// Gets the processor ID this store is bound to.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Loads the checkpoint for this processor.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Loads the checkpoint for this processor at the specified barrier.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadAsync(BarrierId barrierId, CancellationToken ct = default);

    /// <summary>
    /// Loads the checkpoint for this processor at the specified barrier,
    /// resolving positions for the given partitions.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadAsync(
        BarrierId barrierId,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    );

    /// <summary>
    /// Loads the latest checkpoint for this processor,
    /// resolving positions for the given partitions.
    /// </summary>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken ct = default
    );

    /// <summary>
    /// Stores checkpoint positions for the specified partitions, merging with existing positions.
    /// </summary>
    /// <param name="workerId">The worker storing the checkpoint (for attribution).</param>
    /// <param name="barrierId">The barrier that triggered this checkpoint.</param>
    /// <param name="positions">Partition positions to checkpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when positions dictionary is empty.</exception>
    /// <remarks>
    /// <para>
    /// Internally loads the current checkpoint state and merges the provided positions
    /// with existing ones. Uses optimistic concurrency with automatic retry on conflicts.
    /// </para>
    /// <para>
    /// Only the specified partitions are updated; other partitions retain their positions.
    /// </para>
    /// </remarks>
    ValueTask StoreAsync(
        string workerId,
        BarrierId barrierId,
        ImmutableDictionary<Partition, RecordPosition> positions,
        CancellationToken ct = default
    );
}


/// <summary>
/// Checkpoint data for a single partition.
/// </summary>
/// <param name="Position">The record position of the last processed record.</param>
/// <param name="WorkerId">The worker that last checkpointed this partition.</param>
[PublicAPI]
public sealed record PartitionCheckpoint(RecordPosition Position, string WorkerId);

/// <summary>
/// Resolved start positions for creating a subscription.
/// </summary>
/// <param name="StartFrom">
/// The log position to start the subscription from.
/// This is the checkpoint barrier if one exists, otherwise resolved from initial position setting.
/// </param>
/// <param name="Partitions">
/// Per-partition record positions for internal tracking/filtering.
/// Merged from checkpoints and overrides.
/// </param>
[PublicAPI]
public sealed record SubscriptionStartPosition(
    LogPosition StartFrom,
    Dictionary<Partition, LogPosition>[] Partitions
);

/// <summary>
/// Loaded checkpoint for a processor, with resolved positions for all assigned partitions.
/// </summary>
[PublicAPI]
public sealed record ProcessorCheckpoint {
    /// <summary>
    /// Empty checkpoint with no partitions.
    /// </summary>
    public static readonly ProcessorCheckpoint Empty = new();

    /// <summary>
    /// Per-partition checkpoint data including position and worker attribution.
    /// </summary>
    public ImmutableDictionary<Partition, PartitionCheckpoint> Partitions { get; init; } =
        ImmutableDictionary<Partition, PartitionCheckpoint>.Empty;

    /// <summary>
    /// The barrier ID that triggered the last checkpoint.
    /// </summary>
    public BarrierId BarrierId { get; init; } = BarrierId.None;

    /// <summary>
    /// UTC timestamp when the checkpoint was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// Whether any checkpoint data exists.
    /// </summary>
    public bool HasCheckpoint => !Partitions.IsEmpty;

    /// <summary>
    /// The minimum position across all partitions (subscription start position).
    /// </summary>
    public RecordPosition LowWatermark { get; init; } = RecordPosition.Earliest;

    /// <summary>
    /// The maximum position across all partitions.
    /// </summary>
    public RecordPosition HighWatermark { get; init; } = RecordPosition.Earliest;
}
