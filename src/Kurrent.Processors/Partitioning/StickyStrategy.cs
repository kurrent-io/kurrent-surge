using Kurrent.Client.Streams;
using Kurrent.Processors.Models;

namespace Kurrent.Client.Processors.Partitioning;

/// <summary>
/// Distribution strategy that minimizes partition movement during rebalancing.
/// </summary>
/// <remarks>
/// <para>
/// This strategy keeps existing partition assignments stable and only redistributes
/// orphaned partitions (from removed workers or newly added partitions) to the
/// least-loaded workers. This minimizes state transfer during scaling operations.
/// </para>
/// <para>
/// <b>Best for:</b> Production rebalancing when workers join or leave, scenarios
/// where minimizing partition movement is more important than perfectly balanced loads.
/// </para>
/// <para>
/// <b>Algorithm:</b>
/// <list type="number">
///   <item>Existing workers keep their current partitions</item>
///   <item>Orphaned partitions (from removed workers) are collected</item>
///   <item>New partitions (if maxParallelism increased) are collected</item>
///   <item>Orphaned/new partitions are distributed to least-loaded workers</item>
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
        WorkerId[] workers,
        int maxParallelism
    ) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxParallelism, 32768);

        if (workers.Length == 0)
            return DistributionManifest.Empty;

        var workerSet = new HashSet<WorkerId>(workers);
        var transferGroups = new Dictionary<(WorkerId from, WorkerId to), List<Partition>>();

        // Track partitions per worker: kept partitions and load for balancing
        var workerKept = new Dictionary<WorkerId, List<Partition>>();
        var workerGained = new Dictionary<WorkerId, List<Partition>>();
        var workerLost = new Dictionary<WorkerId, List<Partition>>();

        foreach (var worker in workers) {
            workerKept[worker] = [];
            workerGained[worker] = [];
            workerLost[worker] = [];
        }

        // Collect orphaned partitions and determine kept partitions
        var orphanedPartitions = new List<(Partition partition, WorkerId previousOwner)>();
        var assignedPartitions = new HashSet<Partition>();

        foreach (var (workerId, assignment) in current.Assignments) {
            if (workerSet.Contains(workerId)) {
                // Worker still exists - keep its partitions
                foreach (var partition in assignment.All) {
                    if (partition.Value < maxParallelism) {
                        workerKept[workerId].Add(partition);
                        assignedPartitions.Add(partition);
                    } else {
                        // Partition no longer exists (maxParallelism decreased)
                        workerLost[workerId].Add(partition);
                    }
                }
            } else {
                // Worker removed - its partitions are orphaned
                workerLost[workerId] = [];
                foreach (var partition in assignment.All) {
                    if (partition.Value < maxParallelism) {
                        orphanedPartitions.Add((partition, workerId));
                        assignedPartitions.Add(partition); // Track to avoid double-counting as "new"
                    }
                    workerLost[workerId].Add(partition);
                }
            }
        }

        // Collect new partitions (if maxParallelism increased)
        for (var i = 0; i < maxParallelism; i++) {
            var partition = Partition.From(i);
            if (!assignedPartitions.Contains(partition))
                orphanedPartitions.Add((partition, WorkerId.None));
        }

        // Distribute orphaned partitions to least-loaded workers
        foreach (var (partition, previousOwner) in orphanedPartitions) {
            var leastLoadedWorker = GetLeastLoadedWorker(workers, workerKept, workerGained);
            workerGained[leastLoadedWorker].Add(partition);
            AddToGroup(transferGroups, (previousOwner, leastLoadedWorker), partition);
        }

        // Build assignments
        var newAssignments = new Dictionary<WorkerId, PartitionAssignment>();

        foreach (var worker in workers) {
            newAssignments[worker] = new PartitionAssignment(
                [..workerKept[worker]],
                [..workerGained[worker]],
                workerLost.TryGetValue(worker, out var lost) ? [..lost] : []
            );
        }

        // Include removed workers with their losses
        foreach (var (workerId, _) in current.Assignments) {
            if (!workerSet.Contains(workerId) && workerLost.TryGetValue(workerId, out var lost)) {
                newAssignments[workerId] = new PartitionAssignment([], [], [..lost]);
            }
        }

        var transferArray = transferGroups
            .Select(kvp => new PartitionTransfer(kvp.Key.from, kvp.Key.to, [..kvp.Value]))
            .ToArray();

        return new DistributionManifest(newAssignments, transferArray);
    }

    static WorkerId GetLeastLoadedWorker(
        WorkerId[] workers,
        Dictionary<WorkerId, List<Partition>> kept,
        Dictionary<WorkerId, List<Partition>> gained
    ) {
        WorkerId leastLoaded = workers[0];
        var minLoad = kept[leastLoaded].Count + gained[leastLoaded].Count;

        for (var i = 1; i < workers.Length; i++) {
            var worker = workers[i];
            var load = kept[worker].Count + gained[worker].Count;
            if (load < minLoad) {
                minLoad = load;
                leastLoaded = worker;
            }
        }

        return leastLoaded;
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
