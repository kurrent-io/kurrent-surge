using System.Collections.Immutable;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Checkpointing;

public interface ICheckpointStore {
    /// <summary>
    /// Gets the processor ID this store is bound to.
    /// </summary>
    string ProcessorId { get; }

    /// <summary>
    /// Loads the checkpoint for this processor.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The checkpoint containing all partition checkpoints, barrier ID, and resolution logic.
    /// Returns <see cref="ProcessorCheckpoint.Empty"/> if no checkpoint exists.
    /// </returns>
    ValueTask<ProcessorCheckpoint> LoadLatestAsync(CancellationToken ct = default);

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
    ImmutableDictionary<Partition, RecordPosition> Partitions
);

/// <summary>
/// Loaded checkpoint for a processor, with position resolution logic.
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
    public BarrierId BarrierId { get; init; } = BarrierId.Empty;

    /// <summary>
    /// UTC timestamp when the checkpoint was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;

    /// <summary>
    /// Whether any checkpoint data exists.
    /// </summary>
    public bool HasCheckpoint => !Partitions.IsEmpty;

    /// <summary>
    /// Gets start positions for creating a subscription, merging overrides with checkpoints.
    /// </summary>
    /// <param name="initialPosition">
    /// The initial position setting to use when no checkpoint exists.
    /// Determines the barrier position for fresh subscriptions.
    /// </param>
    /// <param name="positionOverrides">
    /// Dictionary of partition-specific start positions that override checkpoints.
    /// Key: Partition value (int), Value: Position (long).
    /// When a partition has an override, the checkpoint for that partition is ignored.
    /// </param>
    public SubscriptionStartPosition ResolveStartPosition(
        SubscriptionInitialPosition initialPosition,
        ImmutableDictionary<int, long> positionOverrides
    ) {
        // Start with checkpoint positions
        var builder = ImmutableDictionary.CreateBuilder<Partition, RecordPosition>();

        foreach (var (partition, checkpoint) in Partitions)
            builder.Add(partition, checkpoint.Position);

        // Apply overrides (convert long to RecordPosition)
        foreach (var (partitionValue, position) in positionOverrides)
            builder[Partition.From(partitionValue)] = RecordPosition.FromLogPosition(position);

        // Resolve the start position for subscription
        // If we have checkpoint data, use the minimum log position from checkpoints
        // Otherwise use the initial position setting
        var startFrom = !HasCheckpoint
            ? initialPosition == SubscriptionInitialPosition.Earliest
                ? LogPosition.Earliest
                : LogPosition.Latest
            : Partitions.Values.Min(cp => cp.Position.LogPosition);

        return new(startFrom, builder.ToImmutable());
    }
}
