using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// The manifest of partition distribution across drones, including change tracking.
/// </summary>
/// <remarks>
/// A manifest captures both the current state (which drone owns which partitions)
/// and the delta from the previous state (what moved where). This enables efficient
/// rebalancing by knowing exactly which partitions need state transfer.
/// </remarks>
[PublicAPI]
public sealed record DistributionManifest {
    /// <summary>
    /// Empty manifest representing no distribution (initial state).
    /// </summary>
    public static readonly DistributionManifest Empty = new(
        new Dictionary<string, PartitionAssignment>(),
        []);

    /// <summary>
    /// Per-drone partition assignments with change tracking.
    /// </summary>
    public IReadOnlyDictionary<string, PartitionAssignment> Assignments { get; }

    /// <summary>
    /// Partition transfers that occurred during this distribution.
    /// </summary>
    public PartitionTransfer[] Transfers { get; }

    /// <summary>
    /// Returns true if no partitions are assigned to any drone.
    /// </summary>
    public bool IsEmpty => Assignments.Count == 0;

    /// <summary>
    /// Returns true if any partitions moved during this distribution.
    /// </summary>
    public bool HasChanges => Transfers.Length > 0;

    /// <summary>
    /// Gets the total number of partitions that moved.
    /// </summary>
    public int TotalMoved => Transfers.Sum(t => t.Partitions.Length);

    /// <summary>
    /// Creates a new distribution manifest.
    /// </summary>
    /// <param name="assignments">Per-drone partition assignments.</param>
    /// <param name="transfers">Partition transfers from previous state.</param>
    public DistributionManifest(
        IReadOnlyDictionary<string, PartitionAssignment> assignments,
        PartitionTransfer[] transfers
    ) {
        Assignments = assignments;
        Transfers   = transfers;
    }

    /// <summary>
    /// Gets the assignment for a specific drone, or <see cref="PartitionAssignment.Empty"/> if not found.
    /// </summary>
    public PartitionAssignment this[string droneId] => Assignments.TryGetValue(droneId, out var assignment) ? assignment : PartitionAssignment.Empty;
}

/// <summary>
/// Per-drone partition assignment with change tracking.
/// </summary>
/// <remarks>
/// Tracks which partitions a drone kept from the previous distribution,
/// which it gained, and which it lost. This enables efficient state transfer
/// during rebalancing.
/// </remarks>
/// <param name="Kept">Partitions retained from the previous assignment.</param>
/// <param name="Gained">Partitions newly assigned to this drone.</param>
/// <param name="Lost">Partitions removed from this drone.</param>
[PublicAPI]
public sealed record PartitionAssignment(
    Partition[] Kept,
    Partition[] Gained,
    Partition[] Lost
) {
    /// <summary>
    /// Empty assignment (no partitions).
    /// </summary>
    public static readonly PartitionAssignment Empty = new([], [], []);

    /// <summary>
    /// All partitions currently assigned to this drone (kept + gained).
    /// </summary>
    public Partition[] All => [..Kept, ..Gained];

    /// <summary>
    /// Returns true if partitions were gained or lost in this distribution.
    /// </summary>
    public bool HasChanges => Gained.Length > 0 || Lost.Length > 0;

    /// <summary>
    /// Total number of partitions currently assigned.
    /// </summary>
    public int Count => Kept.Length + Gained.Length;
}

/// <summary>
/// Describes a transfer of partitions from one drone to another.
/// </summary>
/// <param name="From">The drone losing the partitions (<c>string.Empty</c> if partitions are new).</param>
/// <param name="To">The drone gaining the partitions.</param>
/// <param name="Partitions">The partitions being transferred.</param>
[PublicAPI]
public sealed record PartitionTransfer(
    string From,
    string To,
    Partition[] Partitions
);