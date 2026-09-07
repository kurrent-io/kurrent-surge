using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors.Partitioning;

/// <summary>
/// Distribution strategy that assigns contiguous partition ranges to drones.
/// </summary>
/// <remarks>
/// <para>
/// This strategy distributes partitions as contiguous ranges based on drone order.
/// It's deterministic and produces balanced assignments, but causes high partition
/// movement during rebalancing when drones join or leave.
/// </para>
/// <para>
/// <b>Best for:</b> Initial assignment, testing, scenarios where determinism is more
/// important than minimizing movement.
/// </para>
/// <para>
/// <b>Example with 12 partitions and 3 drones:</b>
/// <list type="bullet">
///   <item>Drone A: [0, 1, 2, 3] (4 partitions)</item>
///   <item>Drone B: [4, 5, 6, 7] (4 partitions)</item>
///   <item>Drone C: [8, 9, 10, 11] (4 partitions)</item>
/// </list>
/// </para>
/// </remarks>
internal sealed class RangeStrategy : IDistributionStrategy {
    /// <summary>
    /// Singleton instance of the range strategy.
    /// </summary>
    public static readonly RangeStrategy Instance = new();

    RangeStrategy() { }

    /// <inheritdoc />
    public DistributionManifest Distribute(
        DistributionManifest current,
        string[] drones,
        int maxParallelism
    ) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParallelism, 32768);

        if (drones.Length == 0)
            return DistributionManifest.Empty;

        // Build reverse lookup from current manifest: partition → owner
        var previousOwner = BuildOwnerLookup(current);

        // Calculate new assignments using contiguous ranges
        var newAssignments = new Dictionary<string, PartitionAssignment>();
        //var transfers = new List<PartitionTransfer>();
        var transferGroups = new Dictionary<(string from, string to), List<Partition>>();

        var droneCount = drones.Length;
        var basePartitions = maxParallelism / droneCount;
        var remainder = maxParallelism % droneCount;
        var currentPartition = 0;

        for (var i = 0; i < droneCount; i++) {
            var droneId = drones[i];
            var partitionCount = basePartitions + (i < remainder ? 1 : 0);

            var kept = new List<Partition>();
            var gained = new List<Partition>();

            for (var j = 0; j < partitionCount; j++) {
                var partition = Partition.From(currentPartition++);
                var prevOwner = previousOwner.GetValueOrDefault(partition, "");

                if (prevOwner == droneId) {
                    kept.Add(partition);
                } else {
                    gained.Add(partition);
                    AddToGroup(transferGroups, (prevOwner, droneId), partition);
                }
            }

            // Calculate lost partitions for this drone
            var lost = new List<Partition>();
            if (current.Assignments.TryGetValue(droneId, out var prevAssignment)) {
                var newPartitions = new HashSet<Partition>(kept.Concat(gained));
                foreach (var partition in prevAssignment.All) {
                    if (!newPartitions.Contains(partition))
                        lost.Add(partition);
                }
            }

            newAssignments[droneId] = new PartitionAssignment(
                [..kept],
                [..gained],
                [..lost]
            );
        }

        // Handle drones that were removed (all their partitions are lost)
        foreach (var (droneId, prevAssignment) in current.Assignments) {
            if (!newAssignments.ContainsKey(droneId)) {
                newAssignments[droneId] = new PartitionAssignment(
                    [],
                    [],
                    prevAssignment.All
                );
            }
        }

        // Convert transfer groups to array
        var transferArray = transferGroups
            .Select(kvp => new PartitionTransfer(kvp.Key.from, kvp.Key.to, [..kvp.Value]))
            .ToArray();

        return new DistributionManifest(newAssignments, transferArray);
    }

    static Dictionary<Partition, string> BuildOwnerLookup(DistributionManifest manifest) {
        var lookup = new Dictionary<Partition, string>();
        foreach (var (droneId, assignment) in manifest.Assignments) {
            foreach (var partition in assignment.All)
                lookup[partition] = droneId;
        }
        return lookup;
    }

    static void AddToGroup(
        Dictionary<(string from, string to), List<Partition>> groups,
        (string from, string to) key,
        Partition partition
    ) {
        if (!groups.TryGetValue(key, out var list))
            groups[key] = list = [];
        list.Add(partition);
    }
}
