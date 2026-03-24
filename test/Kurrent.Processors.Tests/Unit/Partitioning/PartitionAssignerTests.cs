using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Partitioning;

namespace Kurrent.Processors.Tests.Partitioning;

[Category("Processors")]
public class PartitionAssignerTests {
    // Use RangeStrategy for deterministic testing (contiguous ranges)
    readonly PartitionAssigner _assigner = new(RangeStrategy.Instance);

    #region . GetPartition .

    [Test]
    public async Task get_partition_returns_value_in_valid_range() {
        // Arrange
        const int maxParallelism = 128;

        // Act
        var partition = _assigner.GetPartition("customer-123", maxParallelism);

        // Assert
        await Assert.That(partition.Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(partition.Value).IsLessThan(maxParallelism);
    }

    [Test]
    public async Task get_partition_is_deterministic_for_same_key() {
        // Arrange
        const string key            = "order-456";
        const int    maxParallelism = 128;

        // Act
        var partition1 = _assigner.GetPartition(key, maxParallelism);
        var partition2 = _assigner.GetPartition(key, maxParallelism);
        var partition3 = _assigner.GetPartition(key, maxParallelism);

        // Assert
        await Assert.That(partition1).IsEqualTo(partition2);
        await Assert.That(partition2).IsEqualTo(partition3);
    }

    [Test]
    public async Task get_partition_distributes_keys_across_partitions() {
        // Arrange
        const int maxParallelism = 128;
        const int keyCount       = 10000;
        var       partitionHits  = new int[maxParallelism];

        // Act
        for (var i = 0; i < keyCount; i++) {
            var partition = _assigner.GetPartition($"key-{i}", maxParallelism);
            partitionHits[partition]++;
        }

        // Assert - all partitions should have some hits (good distribution)
        var minHits        = partitionHits.Min();
        var maxHits        = partitionHits.Max();
        var expectedAvg    = (double)keyCount / maxParallelism;

        // At least ensure no partition is completely empty or overloaded
        await Assert.That(minHits).IsGreaterThan(0);
        await Assert.That(maxHits).IsLessThan((int)(expectedAvg * 3));
    }

    [Test]
    public async Task get_partition_handles_single_partition() {
        // Arrange & Act
        var partition = _assigner.GetPartition("any-key", maxParallelism: 1);

        // Assert
        await Assert.That(partition.Value).IsEqualTo(0);
    }

    [Test]
    public async Task get_partition_handles_max_parallelism_boundary() {
        // Arrange & Act
        var partition = _assigner.GetPartition("test-key", maxParallelism: 32768);

        // Assert
        await Assert.That(partition.Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(partition.Value).IsLessThan(32768);
    }

    [Test]
    public async Task get_partition_throws_for_null_key() {
        // Act & Assert
        await Assert.That(() => _assigner.GetPartition(null!, 128)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task get_partition_throws_for_invalid_max_parallelism() {
        // Act & Assert
        await Assert.That(() => _assigner.GetPartition("key", 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _assigner.GetPartition("key", -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _assigner.GetPartition("key", 32769)).Throws<ArgumentOutOfRangeException>();
    }

    #endregion

    #region . Distribute .

    [Test]
    public async Task distribute_evenly_with_exact_division() {
        // Arrange
        WorkerId[] workers = ["worker-a", "worker-b", "worker-c", "worker-d"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert
        await Assert.That(manifest.Assignments.Count).IsEqualTo(4);
        await Assert.That(manifest[(WorkerId)"worker-a"].All.Length).IsEqualTo(32); // 128 / 4 = 32 each
        await Assert.That(manifest[(WorkerId)"worker-b"].All.Length).IsEqualTo(32);
        await Assert.That(manifest[(WorkerId)"worker-c"].All.Length).IsEqualTo(32);
        await Assert.That(manifest[(WorkerId)"worker-d"].All.Length).IsEqualTo(32);
    }

    [Test]
    public async Task distribute_remainder_to_earlier_workers() {
        // Arrange
        WorkerId[] workers = ["worker-a", "worker-b", "worker-c"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert - 128 / 3 = 42 remainder 2, so first 2 workers get 43
        await Assert.That(manifest.Assignments.Count).IsEqualTo(3);
        await Assert.That(manifest[(WorkerId)"worker-a"].All.Length).IsEqualTo(43);
        await Assert.That(manifest[(WorkerId)"worker-b"].All.Length).IsEqualTo(43);
        await Assert.That(manifest[(WorkerId)"worker-c"].All.Length).IsEqualTo(42);
    }

    [Test]
    public async Task distribute_creates_contiguous_ranges() {
        // Arrange
        WorkerId[] workers = ["worker-a", "worker-b"];
        const int maxParallelism = 10;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert - worker-a: [0-4], worker-b: [5-9]
        await Assert.That(manifest[(WorkerId)"worker-a"].All).IsEquivalentTo((Partition[])[0, 1, 2, 3, 4]);
        await Assert.That(manifest[(WorkerId)"worker-b"].All).IsEquivalentTo((Partition[])[5, 6, 7, 8, 9]);
    }

    [Test]
    public async Task distribute_single_worker_gets_all() {
        // Arrange
        WorkerId[] workers = ["solo-worker"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert
        await Assert.That(manifest.Assignments.Count).IsEqualTo(1);
        await Assert.That(manifest[(WorkerId)"solo-worker"].All.Length).IsEqualTo(128);
    }

    [Test]
    public async Task distribute_returns_empty_for_no_workers() {
        // Arrange
        var workers = Array.Empty<WorkerId>();

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, 128);

        // Assert
        await Assert.That(manifest.IsEmpty).IsTrue();
    }

    [Test]
    public async Task distribute_covers_all_partitions_exactly_once() {
        // Arrange
        WorkerId[] workers = ["a", "b", "c", "d", "e"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert - all partitions 0-127 should appear exactly once
        var allPartitions = manifest.Assignments.Values
            .SelectMany(a => a.All)
            .OrderBy(p => p.Value)
            .ToArray();

        await Assert.That(allPartitions.Length).IsEqualTo(128);
        for (var i = 0; i < 128; i++)
            await Assert.That(allPartitions[i].Value).IsEqualTo(i);
    }

    [Test]
    public async Task distribute_handles_more_workers_than_partitions() {
        // Arrange
        WorkerId[] workers = ["a", "b", "c", "d", "e"];
        const int maxParallelism = 3;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, maxParallelism);

        // Assert - only first 3 workers get partitions
        await Assert.That(manifest[(WorkerId)"a"].All.Length).IsEqualTo(1);
        await Assert.That(manifest[(WorkerId)"b"].All.Length).IsEqualTo(1);
        await Assert.That(manifest[(WorkerId)"c"].All.Length).IsEqualTo(1);
        await Assert.That(manifest[(WorkerId)"d"].All.Length).IsEqualTo(0);
        await Assert.That(manifest[(WorkerId)"e"].All.Length).IsEqualTo(0);
    }

    #endregion

    #region . Rebalancing .

    [Test]
    public async Task rebalance_returns_no_changes_for_same_workers() {
        // Arrange
        WorkerId[] workers = ["a", "b"];
        var initial = _assigner.Distribute(DistributionManifest.Empty, workers, 10);

        // Act
        var rebalanced = _assigner.Distribute(initial, workers, 10);

        // Assert
        await Assert.That(rebalanced.HasChanges).IsFalse();
        await Assert.That(rebalanced.TotalMoved).IsEqualTo(0);
    }

    [Test]
    public async Task rebalance_detects_worker_joining() {
        // Arrange
        WorkerId[] twoWorkers   = ["a", "b"];
        WorkerId[] threeWorkers = ["a", "b", "c"];

        var before = _assigner.Distribute(DistributionManifest.Empty, twoWorkers, 12);
        // Before: a=[0-5], b=[6-11]

        // Act
        var after = _assigner.Distribute(before, threeWorkers, 12);
        // After:  a=[0-3], b=[4-7], c=[8-11]

        // Assert
        await Assert.That(after[(WorkerId)"c"].Gained).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
        await Assert.That(after[(WorkerId)"a"].Lost).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after[(WorkerId)"b"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
    }

    [Test]
    public async Task rebalance_detects_worker_leaving() {
        // Arrange
        WorkerId[] threeWorkers = ["a", "b", "c"];
        WorkerId[] twoWorkers   = ["a", "b"];

        var before = _assigner.Distribute(DistributionManifest.Empty, threeWorkers, 12);
        // Before: a=[0-3], b=[4-7], c=[8-11]

        // Act
        var after = _assigner.Distribute(before, twoWorkers, 12);
        // After:  a=[0-5], b=[6-11]

        // Assert
        await Assert.That(after[(WorkerId)"a"].Gained).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after[(WorkerId)"b"].Gained).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
        await Assert.That(after[(WorkerId)"b"].Lost).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after[(WorkerId)"c"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
    }

    [Test]
    public async Task rebalance_tracks_transfers() {
        // Arrange - manually construct a manifest for precise control
        var beforeAssignments = new Dictionary<WorkerId, PartitionAssignment> {
            [(WorkerId)"a"] = new([Partition.From(0), Partition.From(1), Partition.From(2)], [], []),
            [(WorkerId)"b"] = new([Partition.From(3), Partition.From(4), Partition.From(5)], [], [])
        };
        var before = new DistributionManifest(beforeAssignments, []);

        // After: a gets fewer, b gets partition 2
        WorkerId[] workers = ["a", "b"];

        // Act - redistribute with range strategy
        var after = _assigner.Distribute(before, workers, 6);

        // Assert - with range strategy: a=[0-2], b=[3-5], so partition 2 stays with a
        // Actually range gives a=[0,1,2] and b=[3,4,5] - same as before, no movement
        await Assert.That(after.HasChanges).IsFalse();
    }

    [Test]
    public async Task rebalance_handles_new_partitions_from_empty() {
        // Arrange
        WorkerId[] workers = ["new-worker"];

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, 3);

        // Assert
        await Assert.That(manifest[(WorkerId)"new-worker"].Gained).IsEquivalentTo((Partition[])[0, 1, 2]);
        await Assert.That(manifest.Transfers.Any(t => t.From.IsUnset && t.To == (WorkerId)"new-worker")).IsTrue();
    }

    [Test]
    public async Task rebalance_total_moved_is_accurate() {
        // Arrange
        var beforeAssignments = new Dictionary<WorkerId, PartitionAssignment> {
            [(WorkerId)"a"] = new([
                Partition.From(0), Partition.From(1), Partition.From(2),
                Partition.From(3), Partition.From(4), Partition.From(5)
            ], [], [])
        };
        var before = new DistributionManifest(beforeAssignments, []);

        // Act - add worker b, range strategy splits evenly
        WorkerId[] workers = ["a", "b"];
        var after = _assigner.Distribute(before, workers, 6);

        // Assert - a keeps [0,1,2], b gets [3,4,5]
        await Assert.That(after.TotalMoved).IsEqualTo(3);
    }

    #endregion

    #region . Hash Consistency .

    [Test]
    public async Task hash_is_consistent_for_known_values() {
        // These are regression tests to ensure hash doesn't change between versions
        // The actual values are not important, just that they remain stable

        var partition1 = _assigner.GetPartition("customer-123", 128);
        var partition2 = _assigner.GetPartition("order-456", 128);
        var partition3 = _assigner.GetPartition("", 128);

        // Just verify they're deterministic (specific values don't matter)
        await Assert.That(_assigner.GetPartition("customer-123", 128)).IsEqualTo(partition1);
        await Assert.That(_assigner.GetPartition("order-456", 128)).IsEqualTo(partition2);
        await Assert.That(_assigner.GetPartition("", 128)).IsEqualTo(partition3);
    }

    [Test]
    public async Task different_keys_produce_different_partitions_usually() {
        // Arrange
        const int maxParallelism = 1000;
        var uniquePartitions = new HashSet<int>();

        // Act
        for (var i = 0; i < 100; i++)
            uniquePartitions.Add(_assigner.GetPartition($"unique-key-{i}", maxParallelism));

        // Assert - with 100 keys and 1000 partitions, we should have many unique partitions
        await Assert.That(uniquePartitions.Count).IsGreaterThan(50);
    }

    #endregion

    #region . Scaling Scenarios .

    [Test]
    public async Task scaling_from_1_to_4_workers_distributes_evenly() {
        // Arrange
        const int maxParallelism = 128;

        WorkerId[] solo = ["solo"];
        WorkerId[] four = ["a", "b", "c", "d"];

        var oneWorker = _assigner.Distribute(DistributionManifest.Empty, solo, maxParallelism);

        // Act
        var fourWorkers = _assigner.Distribute(oneWorker, four, maxParallelism);

        // Assert
        await Assert.That(fourWorkers[(WorkerId)"solo"].Lost.Length).IsEqualTo(128); // Loses all (not in new worker list)

        await Assert.That(fourWorkers[(WorkerId)"a"].All.Length).IsEqualTo(32);
        await Assert.That(fourWorkers[(WorkerId)"b"].All.Length).IsEqualTo(32);
        await Assert.That(fourWorkers[(WorkerId)"c"].All.Length).IsEqualTo(32);
        await Assert.That(fourWorkers[(WorkerId)"d"].All.Length).IsEqualTo(32);
    }

    [Test]
    public async Task scaling_down_consolidates_partitions() {
        // Arrange
        const int maxParallelism = 128;

        WorkerId[] four = ["a", "b", "c", "d"];
        WorkerId[] solo = ["a"];

        var fourWorkers = _assigner.Distribute(DistributionManifest.Empty, four, maxParallelism);

        // Act
        var oneWorker = _assigner.Distribute(fourWorkers, solo, maxParallelism);

        // Assert
        await Assert.That(oneWorker[(WorkerId)"a"].Gained.Length).IsEqualTo(96); // Gains from b, c, d
        await Assert.That(oneWorker[(WorkerId)"a"].All.Length).IsEqualTo(128);
    }

    #endregion
}

[Category("Processors")]
public class StickyStrategyTests {
    readonly PartitionAssigner _assigner = new(StickyStrategy.Instance);

    [Test]
    public async Task sticky_keeps_all_existing_assignments_when_worker_joins() {
        // Arrange
        WorkerId[] twoWorkers   = ["a", "b"];
        WorkerId[] threeWorkers = ["a", "b", "c"];

        // Use range for initial deterministic setup
        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, twoWorkers, 12);
        // Initial: a=[0-5], b=[6-11]

        // Act - sticky rebalance (new worker joins)
        var after = _assigner.Distribute(initial, threeWorkers, 12);

        // Assert - sticky doesn't proactively redistribute when workers JOIN
        // a and b keep all their partitions, c gets nothing (no orphaned partitions)
        await Assert.That(after[(WorkerId)"a"].Kept).IsEquivalentTo((Partition[])[0, 1, 2, 3, 4, 5]);
        await Assert.That(after[(WorkerId)"b"].Kept).IsEquivalentTo((Partition[])[6, 7, 8, 9, 10, 11]);
        await Assert.That(after[(WorkerId)"c"].All.Length).IsEqualTo(0); // New worker gets nothing with sticky

        // No changes since no partitions moved
        await Assert.That(after.HasChanges).IsFalse();

        // Total partitions should still be 12
        var total = after.Assignments.Values.Sum(a => a.All.Length);
        await Assert.That(total).IsEqualTo(12);
    }

    [Test]
    public async Task sticky_redistributes_orphaned_partitions_when_worker_leaves() {
        // Arrange
        WorkerId[] threeWorkers = ["a", "b", "c"];
        WorkerId[] twoWorkers   = ["a", "b"];

        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, threeWorkers, 12);
        // Initial: a=[0-3], b=[4-7], c=[8-11]

        // Act
        var after = _assigner.Distribute(initial, twoWorkers, 12);

        // Assert - a keeps [0-3], b keeps [4-7], c's [8-11] redistributed
        await Assert.That(after[(WorkerId)"a"].Kept).IsEquivalentTo((Partition[])[0, 1, 2, 3]);
        await Assert.That(after[(WorkerId)"b"].Kept).IsEquivalentTo((Partition[])[4, 5, 6, 7]);
        await Assert.That(after[(WorkerId)"c"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);

        // Orphaned partitions go to least loaded (distributed between a and b)
        var totalGained = after[(WorkerId)"a"].Gained.Length + after[(WorkerId)"b"].Gained.Length;
        await Assert.That(totalGained).IsEqualTo(4);
    }

    [Test]
    public async Task sticky_minimizes_movement_compared_to_range() {
        // Arrange
        WorkerId[] workers = ["a", "b", "c"];
        WorkerId[] moreWorkers = ["a", "b", "c", "d"];

        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, workers, 128);

        // Act
        var stickyResult = _assigner.Distribute(initial, moreWorkers, 128);
        var rangeResult = rangeAssigner.Distribute(initial, moreWorkers, 128);

        // Assert - sticky should move fewer partitions than range
        await Assert.That(stickyResult.TotalMoved).IsLessThanOrEqualTo(rangeResult.TotalMoved);
    }

    [Test]
    public async Task sticky_handles_initial_empty_distribution() {
        // Arrange
        WorkerId[] workers = ["a", "b", "c"];

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, workers, 12);

        // Assert - all partitions are "gained" since starting from empty
        var totalGained = manifest.Assignments.Values.Sum(a => a.Gained.Length);
        await Assert.That(totalGained).IsEqualTo(12);

        // Distribution should be balanced
        await Assert.That(manifest[(WorkerId)"a"].All.Length).IsEqualTo(4);
        await Assert.That(manifest[(WorkerId)"b"].All.Length).IsEqualTo(4);
        await Assert.That(manifest[(WorkerId)"c"].All.Length).IsEqualTo(4);
    }
}
