using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Distribution strategy that assigns contiguous partition ranges to workers.
/// </summary>
/// <remarks>
/// <para>
/// This strategy distributes partitions as contiguous ranges based on worker order.
/// It's deterministic and produces balanced assignments, but causes high partition
/// movement during rebalancing when workers join or leave.
/// </para>
/// <para>
/// <b>Best for:</b> Initial assignment, testing, scenarios where determinism is more
/// important than minimizing movement.
/// </para>
/// <para>
/// <b>Example with 12 partitions and 3 workers:</b>
/// <list type="bullet">
///   <item>Worker A: [0, 1, 2, 3] (4 partitions)</item>
///   <item>Worker B: [4, 5, 6, 7] (4 partitions)</item>
///   <item>Worker C: [8, 9, 10, 11] (4 partitions)</item>
/// </list>
/// </para>
/// </remarks>
[PublicAPI]
public sealed class RangeStrategy : IDistributionStrategy {
    /// <summary>
    /// Singleton instance of the range strategy.
    /// </summary>
    public static readonly RangeStrategy Instance = new();

    RangeStrategy() { }

    /// <inheritdoc />
    public DistributionManifest Distribute(
        DistributionManifest current,
        WorkerId[] workers,
        int maxParallelism
    ) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParallelism, 32768);

        if (workers.Length == 0)
            return DistributionManifest.Empty;

        // Build reverse lookup from current manifest: partition → owner
        var previousOwner = BuildOwnerLookup(current);

        // Calculate new assignments using contiguous ranges
        var newAssignments = new Dictionary<WorkerId, PartitionAssignment>();
        var transfers = new List<PartitionTransfer>();
        var transferGroups = new Dictionary<(WorkerId from, WorkerId to), List<Partition>>();

        var workerCount = workers.Length;
        var basePartitions = maxParallelism / workerCount;
        var remainder = maxParallelism % workerCount;
        var currentPartition = 0;

        for (var i = 0; i < workerCount; i++) {
            var workerId = workers[i];
            var partitionCount = basePartitions + (i < remainder ? 1 : 0);

            var kept = new List<Partition>();
            var gained = new List<Partition>();

            for (var j = 0; j < partitionCount; j++) {
                var partition = Partition.From(currentPartition++);
                var prevOwner = previousOwner.GetValueOrDefault(partition, WorkerId.None);

                if (prevOwner == workerId) {
                    kept.Add(partition);
                } else {
                    gained.Add(partition);
                    AddToGroup(transferGroups, (prevOwner, workerId), partition);
                }
            }

            // Calculate lost partitions for this worker
            var lost = new List<Partition>();
            if (current.Assignments.TryGetValue(workerId, out var prevAssignment)) {
                var newPartitions = new HashSet<Partition>(kept.Concat(gained));
                foreach (var partition in prevAssignment.All) {
                    if (!newPartitions.Contains(partition))
                        lost.Add(partition);
                }
            }

            newAssignments[workerId] = new PartitionAssignment(
                [..kept],
                [..gained],
                [..lost]
            );
        }

        // Handle workers that were removed (all their partitions are lost)
        foreach (var (workerId, prevAssignment) in current.Assignments) {
            if (!newAssignments.ContainsKey(workerId)) {
                newAssignments[workerId] = new PartitionAssignment(
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

    static Dictionary<Partition, WorkerId> BuildOwnerLookup(DistributionManifest manifest) {
        var lookup = new Dictionary<Partition, WorkerId>();
        foreach (var (workerId, assignment) in manifest.Assignments) {
            foreach (var partition in assignment.All)
                lookup[partition] = workerId;
        }
        return lookup;
    }

    static void AddToGroup(
        Dictionary<(WorkerId from, WorkerId to), List<Partition>> groups,
        (WorkerId from, WorkerId to) key,
        Partition partition
    ) {
        if (!groups.TryGetValue(key, out var list))
            groups[key] = list = [];
        list.Add(partition);
    }
}
