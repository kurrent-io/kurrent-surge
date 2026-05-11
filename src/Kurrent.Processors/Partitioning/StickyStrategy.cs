using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Distribution strategy that minimizes partition movement during rebalancing.
/// </summary>
/// <remarks>
/// <para>
/// This strategy keeps existing partition assignments stable and only redistributes
/// orphaned partitions (from removed drones or newly added partitions) to the
/// least-loaded drones. This minimizes state transfer during scaling operations.
/// </para>
/// <para>
/// <b>Best for:</b> Production rebalancing when drones join or leave, scenarios
/// where minimizing partition movement is more important than perfectly balanced loads.
/// </para>
/// <para>
/// <b>Algorithm:</b>
/// <list type="number">
///   <item>Existing drones keep their current partitions</item>
///   <item>Orphaned partitions (from removed drones) are collected</item>
///   <item>New partitions (if maxParallelism increased) are collected</item>
///   <item>Orphaned/new partitions are distributed to least-loaded drones</item>
/// </list>
/// </para>
/// </remarks>
[PublicAPI]
public sealed class StickyStrategy : IDistributionStrategy {
    /// <summary>
    /// Singleton instance of the sticky strategy.
    /// </summary>
    public static readonly StickyStrategy Instance = new();

    StickyStrategy() { }

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

        var droneSet = new HashSet<string>(drones);
        var transferGroups = new Dictionary<(string from, string to), List<Partition>>();

        // Track partitions per drone: kept partitions and load for balancing
        var droneKept = new Dictionary<string, List<Partition>>();
        var droneGained = new Dictionary<string, List<Partition>>();
        var droneLost = new Dictionary<string, List<Partition>>();

        foreach (var drone in drones) {
            droneKept[drone] = [];
            droneGained[drone] = [];
            droneLost[drone] = [];
        }

        // Collect orphaned partitions and determine kept partitions
        var orphanedPartitions = new List<(Partition partition, string previousOwner)>();
        var assignedPartitions = new HashSet<Partition>();

        foreach (var (droneId, assignment) in current.Assignments) {
            if (droneSet.Contains(droneId)) {
                // Drone still exists - keep its partitions
                foreach (var partition in assignment.All) {
                    if (partition.Value < maxParallelism) {
                        droneKept[droneId].Add(partition);
                        assignedPartitions.Add(partition);
                    } else {
                        // Partition no longer exists (maxParallelism decreased)
                        droneLost[droneId].Add(partition);
                    }
                }
            } else {
                // Drone removed - its partitions are orphaned
                droneLost[droneId] = [];
                foreach (var partition in assignment.All) {
                    if (partition.Value < maxParallelism) {
                        orphanedPartitions.Add((partition, droneId));
                        assignedPartitions.Add(partition); // Track to avoid double-counting as "new"
                    }
                    droneLost[droneId].Add(partition);
                }
            }
        }

        // Collect new partitions (if maxParallelism increased)
        for (var i = 0; i < maxParallelism; i++) {
            var partition = Partition.From(i);
            if (!assignedPartitions.Contains(partition))
                orphanedPartitions.Add((partition, ""));
        }

        // Distribute orphaned partitions to least-loaded drones
        foreach (var (partition, previousOwner) in orphanedPartitions) {
            var leastLoadedDrone = GetLeastLoadedDrone(drones, droneKept, droneGained);
            droneGained[leastLoadedDrone].Add(partition);
            AddToGroup(transferGroups, (previousOwner, leastLoadedDrone), partition);
        }

        // Build assignments
        var newAssignments = new Dictionary<string, PartitionAssignment>();

        foreach (var drone in drones) {
            newAssignments[drone] = new PartitionAssignment(
                [..droneKept[drone]],
                [..droneGained[drone]],
                droneLost.TryGetValue(drone, out var lost) ? [..lost] : []
            );
        }

        // Include removed drones with their losses
        foreach (var (droneId, _) in current.Assignments) {
            if (!droneSet.Contains(droneId) && droneLost.TryGetValue(droneId, out var lost)) {
                newAssignments[droneId] = new PartitionAssignment([], [], [..lost]);
            }
        }

        var transferArray = transferGroups
            .Select(kvp => new PartitionTransfer(kvp.Key.from, kvp.Key.to, [..kvp.Value]))
            .ToArray();

        return new DistributionManifest(newAssignments, transferArray);
    }

    static string GetLeastLoadedDrone(
        string[] drones,
        Dictionary<string, List<Partition>> kept,
        Dictionary<string, List<Partition>> gained
    ) {
        string leastLoadedDrone = drones[0];
        var minLoad = kept[leastLoadedDrone].Count + gained[leastLoadedDrone].Count;

        for (var i = 1; i < drones.Length; i++) {
            var drone = drones[i];
            var load = kept[drone].Count + gained[drone].Count;
            if (load < minLoad) {
                minLoad = load;
                leastLoadedDrone = drone;
            }
        }

        return leastLoadedDrone;
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
