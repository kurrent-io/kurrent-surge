using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Checkpoint data for a single partition.
/// </summary>
/// <param name="Position">The log position of the last processed record.</param>
/// <param name="WorkerId">The worker that last checkpointed this partition.</param>
[PublicAPI]
public sealed record PartitionCheckpoint(LogPosition Position, string WorkerId);

/// <summary>
/// Resolved start positions for creating a subscription.
/// </summary>
/// <param name="StartFrom">
/// The position to start the subscription from.
/// This is the checkpoint barrier if one exists, otherwise resolved from initial position setting.
/// </param>
/// <param name="Partitions">
/// Per-partition positions for internal tracking/filtering.
/// Merged from checkpoints and overrides.
/// </param>
[PublicAPI]
public sealed record SubscriptionStartPosition(
    LogPosition StartFrom,
    ImmutableDictionary<Partition, LogPosition> Partitions
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
    public LogPosition BarrierId { get; init; } = LogPosition.Unset;

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
        var builder = ImmutableDictionary.CreateBuilder<Partition, LogPosition>();

        foreach (var (partition, checkpoint) in Partitions)
            builder.Add(partition, checkpoint.Position);

        // Apply overrides
        foreach (var (partitionValue, position) in positionOverrides)
            builder[Partition.From(partitionValue)] = position;

        // Resolve the start position
        var startFrom = BarrierId == LogPosition.Unset
            ? initialPosition == SubscriptionInitialPosition.Earliest
                ? LogPosition.Earliest
                : LogPosition.Latest
            : BarrierId;

        return new(startFrom, builder.ToImmutable());
    }
}
