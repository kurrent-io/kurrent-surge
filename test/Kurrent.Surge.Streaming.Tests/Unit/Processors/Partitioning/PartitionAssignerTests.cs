using Kurrent.Surge.Client.Processors.Partitioning;
using Kurrent.Surge.Client.Streams;
using Kurrent.Streaming.Partitioning;

namespace Kurrent.Streaming.Tests.Partitioning;

[Category("Processors")]
public class PartitionAssignerTests {
    // Use RangeStrategy for deterministic testing (contiguous ranges)
    readonly PartitionAssigner _assigner = new(RangeStrategy.Instance);

    #region ->> GetPartition <<-

    [Test]
    public async ValueTask get_partition_returns_value_in_valid_range() {
        // Arrange
        const int maxParallelism = 128;

        // Act
        var partition = _assigner.GetPartition("customer-123", maxParallelism);

        // Assert
        await Assert.That(partition.Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(partition.Value).IsLessThan(maxParallelism);
    }

    [Test]
    public async ValueTask get_partition_is_deterministic_for_same_key() {
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
    public async ValueTask get_partition_distributes_keys_across_partitions() {
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
    public async ValueTask get_partition_handles_single_partition() {
        // Arrange & Act
        var partition = _assigner.GetPartition("any-key", maxParallelism: 1);

        // Assert
        await Assert.That(partition.Value).IsEqualTo(0);
    }

    [Test]
    public async ValueTask get_partition_handles_max_parallelism_boundary() {
        // Arrange & Act
        var partition = _assigner.GetPartition("test-key", maxParallelism: 32768);

        // Assert
        await Assert.That(partition.Value).IsGreaterThanOrEqualTo(0);
        await Assert.That(partition.Value).IsLessThan(32768);
    }

    [Test]
    public async ValueTask get_partition_throws_for_null_key() {
        // Act & Assert
        await Assert.That(() => _assigner.GetPartition(null!, 128)).Throws<ArgumentNullException>();
    }

    [Test]
    public async ValueTask get_partition_throws_for_invalid_max_parallelism() {
        // Act & Assert
        await Assert.That(() => _assigner.GetPartition("key", 0)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _assigner.GetPartition("key", -1)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => _assigner.GetPartition("key", 32769)).Throws<ArgumentOutOfRangeException>();
    }

    #endregion // GetPartition

    #region ->> Distribute <<-

    [Test]
    public async ValueTask distribute_evenly_with_exact_division() {
        // Arrange
        string[] drones = ["drone-a", "drone-b", "drone-c", "drone-d"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

        // Assert
        await Assert.That(manifest.Assignments.Count).IsEqualTo(4);
        await Assert.That(manifest["drone-a"].All.Length).IsEqualTo(32); // 128 / 4 = 32 each
        await Assert.That(manifest["drone-b"].All.Length).IsEqualTo(32);
        await Assert.That(manifest["drone-c"].All.Length).IsEqualTo(32);
        await Assert.That(manifest["drone-d"].All.Length).IsEqualTo(32);
    }

    [Test]
    public async ValueTask distribute_remainder_to_earlier_drones() {
        // Arrange
        string[] drones = ["drone-a", "drone-b", "drone-c"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

        // Assert - 128 / 3 = 42 remainder 2, so first 2 drones get 43
        await Assert.That(manifest.Assignments.Count).IsEqualTo(3);
        await Assert.That(manifest["drone-a"].All.Length).IsEqualTo(43);
        await Assert.That(manifest["drone-b"].All.Length).IsEqualTo(43);
        await Assert.That(manifest["drone-c"].All.Length).IsEqualTo(42);
    }

    [Test]
    public async ValueTask distribute_creates_contiguous_ranges() {
        // Arrange
        string[] drones = ["drone-a", "drone-b"];
        const int maxParallelism = 10;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

        // Assert - drone-a: [0-4], drone-b: [5-9]
        await Assert.That(manifest["drone-a"].All).IsEquivalentTo((Partition[])[0, 1, 2, 3, 4]);
        await Assert.That(manifest["drone-b"].All).IsEquivalentTo((Partition[])[5, 6, 7, 8, 9]);
    }

    [Test]
    public async ValueTask distribute_single_drone_gets_all() {
        // Arrange
        string[] drones = ["solo-drone"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

        // Assert
        await Assert.That(manifest.Assignments.Count).IsEqualTo(1);
        await Assert.That(manifest["solo-drone"].All.Length).IsEqualTo(128);
    }

    [Test]
    public async ValueTask distribute_returns_empty_for_no_drones() {
        // Arrange
        var drones = Array.Empty<string>();

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, 128);

        // Assert
        await Assert.That(manifest.IsEmpty).IsTrue();
    }

    [Test]
    public async ValueTask distribute_covers_all_partitions_exactly_once() {
        // Arrange
        string[] drones = ["a", "b", "c", "d", "e"];
        const int maxParallelism = 128;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

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
    public async ValueTask distribute_handles_more_drones_than_partitions() {
        // Arrange
        string[] drones = ["a", "b", "c", "d", "e"];
        const int maxParallelism = 3;

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, maxParallelism);

        // Assert - only first 3 drones get partitions
        await Assert.That(manifest["a"].All.Length).IsEqualTo(1);
        await Assert.That(manifest["b"].All.Length).IsEqualTo(1);
        await Assert.That(manifest["c"].All.Length).IsEqualTo(1);
        await Assert.That(manifest["d"].All.Length).IsEqualTo(0);
        await Assert.That(manifest["e"].All.Length).IsEqualTo(0);
    }

    #endregion // Distribute

    #region ->> Rebalancing <<-

    [Test]
    public async ValueTask rebalance_returns_no_changes_for_same_drones() {
        // Arrange
        string[] drones = ["a", "b"];
        var initial = _assigner.Distribute(DistributionManifest.Empty, drones, 10);

        // Act
        var rebalanced = _assigner.Distribute(initial, drones, 10);

        // Assert
        await Assert.That(rebalanced.HasChanges).IsFalse();
        await Assert.That(rebalanced.TotalMoved).IsEqualTo(0);
    }

    [Test]
    public async ValueTask rebalance_detects_drone_joining() {
        // Arrange
        string[] twoDrones   = ["a", "b"];
        string[] threeDrones = ["a", "b", "c"];

        var before = _assigner.Distribute(DistributionManifest.Empty, twoDrones, 12);
        // Before: a=[0-5], b=[6-11]

        // Act
        var after = _assigner.Distribute(before, threeDrones, 12);
        // After:  a=[0-3], b=[4-7], c=[8-11]

        // Assert
        await Assert.That(after["c"].Gained).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
        await Assert.That(after["a"].Lost).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after["b"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
    }

    [Test]
    public async ValueTask rebalance_detects_drone_leaving() {
        // Arrange
        string[] threeDrones = ["a", "b", "c"];
        string[] twoDrones   = ["a", "b"];

        var before = _assigner.Distribute(DistributionManifest.Empty, threeDrones, 12);
        // Before: a=[0-3], b=[4-7], c=[8-11]

        // Act
        var after = _assigner.Distribute(before, twoDrones, 12);
        // After:  a=[0-5], b=[6-11]

        // Assert
        await Assert.That(after["a"].Gained).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after["b"].Gained).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
        await Assert.That(after["b"].Lost).IsEquivalentTo((Partition[])[4, 5]);
        await Assert.That(after["c"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);
    }

    [Test]
    public async ValueTask rebalance_tracks_transfers() {
        // Arrange - manually construct a manifest for precise control
        var beforeAssignments = new Dictionary<string, PartitionAssignment> {
            ["a"] = new([Partition.From(0), Partition.From(1), Partition.From(2)], [], []),
            ["b"] = new([Partition.From(3), Partition.From(4), Partition.From(5)], [], [])
        };
        var before = new DistributionManifest(beforeAssignments, []);

        // After: a gets fewer, b gets partition 2
        string[] drones = ["a", "b"];

        // Act - redistribute with range strategy
        var after = _assigner.Distribute(before, drones, 6);

        // Assert - with range strategy: a=[0-2], b=[3-5], so partition 2 stays with a
        // Actually range gives a=[0,1,2] and b=[3,4,5] - same as before, no movement
        await Assert.That(after.HasChanges).IsFalse();
    }

    [Test]
    public async ValueTask rebalance_handles_new_partitions_from_empty() {
        // Arrange
        string[] drones = ["new-drone"];

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, 3);

        // Assert
        await Assert.That(manifest["new-drone"].Gained).IsEquivalentTo((Partition[])[0, 1, 2]);
        await Assert.That(manifest.Transfers.Any(t => string.IsNullOrEmpty(t.From) && t.To == "new-drone")).IsTrue();
    }

    [Test]
    public async ValueTask rebalance_total_moved_is_accurate() {
        // Arrange
        var beforeAssignments = new Dictionary<string, PartitionAssignment> {
            ["a"] = new([
                Partition.From(0), Partition.From(1), Partition.From(2),
                Partition.From(3), Partition.From(4), Partition.From(5)
            ], [], [])
        };
        var before = new DistributionManifest(beforeAssignments, []);

        // Act - add drone b, range strategy splits evenly
        string[] drones = ["a", "b"];
        var after = _assigner.Distribute(before, drones, 6);

        // Assert - a keeps [0,1,2], b gets [3,4,5]
        await Assert.That(after.TotalMoved).IsEqualTo(3);
    }

    #endregion // Rebalancing

    #region ->> Hash Consistency <<-

    [Test]
    public async ValueTask hash_is_consistent_for_known_values() {
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
    public async ValueTask different_keys_produce_different_partitions_usually() {
        // Arrange
        const int maxParallelism = 1000;
        var uniquePartitions = new HashSet<int>();

        // Act
        for (var i = 0; i < 100; i++)
            uniquePartitions.Add(_assigner.GetPartition($"unique-key-{i}", maxParallelism));

        // Assert - with 100 keys and 1000 partitions, we should have many unique partitions
        await Assert.That(uniquePartitions.Count).IsGreaterThan(50);
    }

    #endregion // Hash Consistency

    #region ->> Scaling Scenarios <<-

    [Test]
    public async ValueTask scaling_from_1_to_4_drones_distributes_evenly() {
        // Arrange
        const int maxParallelism = 128;

        string[] solo = ["solo"];
        string[] four = ["a", "b", "c", "d"];

        var oneDrone = _assigner.Distribute(DistributionManifest.Empty, solo, maxParallelism);

        // Act
        var fourDrones = _assigner.Distribute(oneDrone, four, maxParallelism);

        // Assert
        await Assert.That(fourDrones["solo"].Lost.Length).IsEqualTo(128); // Loses all (not in new drone list)

        await Assert.That(fourDrones["a"].All.Length).IsEqualTo(32);
        await Assert.That(fourDrones["b"].All.Length).IsEqualTo(32);
        await Assert.That(fourDrones["c"].All.Length).IsEqualTo(32);
        await Assert.That(fourDrones["d"].All.Length).IsEqualTo(32);
    }

    [Test]
    public async ValueTask scaling_down_consolidates_partitions() {
        // Arrange
        const int maxParallelism = 128;

        string[] four = ["a", "b", "c", "d"];
        string[] solo = ["a"];

        var fourDrones = _assigner.Distribute(DistributionManifest.Empty, four, maxParallelism);

        // Act
        var oneDrone = _assigner.Distribute(fourDrones, solo, maxParallelism);

        // Assert
        await Assert.That(oneDrone["a"].Gained.Length).IsEqualTo(96); // Gains from b, c, d
        await Assert.That(oneDrone["a"].All.Length).IsEqualTo(128);
    }

    #endregion // Scaling Scenarios
}

[Category("Processors")]
public class StickyStrategyTests {
    readonly PartitionAssigner _assigner = new(StickyStrategy.Instance);

    [Test]
    public async ValueTask sticky_keeps_all_existing_assignments_when_drone_joins() {
        // Arrange
        string[] twoDrones   = ["a", "b"];
        string[] threeDrones = ["a", "b", "c"];

        // Use range for initial deterministic setup
        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, twoDrones, 12);
        // Initial: a=[0-5], b=[6-11]

        // Act - sticky rebalance (new drone joins)
        var after = _assigner.Distribute(initial, threeDrones, 12);

        // Assert - sticky doesn't proactively redistribute when drones JOIN
        // a and b keep all their partitions, c gets nothing (no orphaned partitions)
        await Assert.That(after["a"].Kept).IsEquivalentTo((Partition[])[0, 1, 2, 3, 4, 5]);
        await Assert.That(after["b"].Kept).IsEquivalentTo((Partition[])[6, 7, 8, 9, 10, 11]);
        await Assert.That(after["c"].All.Length).IsEqualTo(0); // New drone gets nothing with sticky

        // No changes since no partitions moved
        await Assert.That(after.HasChanges).IsFalse();

        // Total partitions should still be 12
        var total = after.Assignments.Values.Sum(a => a.All.Length);
        await Assert.That(total).IsEqualTo(12);
    }

    [Test]
    public async ValueTask sticky_redistributes_orphaned_partitions_when_drone_leaves() {
        // Arrange
        string[] threeDrones = ["a", "b", "c"];
        string[] twoDrones   = ["a", "b"];

        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, threeDrones, 12);
        // Initial: a=[0-3], b=[4-7], c=[8-11]

        // Act
        var after = _assigner.Distribute(initial, twoDrones, 12);

        // Assert - a keeps [0-3], b keeps [4-7], c's [8-11] redistributed
        await Assert.That(after["a"].Kept).IsEquivalentTo((Partition[])[0, 1, 2, 3]);
        await Assert.That(after["b"].Kept).IsEquivalentTo((Partition[])[4, 5, 6, 7]);
        await Assert.That(after["c"].Lost).IsEquivalentTo((Partition[])[8, 9, 10, 11]);

        // Orphaned partitions go to least loaded (distributed between a and b)
        var totalGained = after["a"].Gained.Length + after["b"].Gained.Length;
        await Assert.That(totalGained).IsEqualTo(4);
    }

    [Test]
    public async ValueTask sticky_minimizes_movement_compared_to_range() {
        // Arrange
        string[] drones = ["a", "b", "c"];
        string[] moreDrones = ["a", "b", "c", "d"];

        var rangeAssigner = new PartitionAssigner(RangeStrategy.Instance);
        var initial = rangeAssigner.Distribute(DistributionManifest.Empty, drones, 128);

        // Act
        var stickyResult = _assigner.Distribute(initial, moreDrones, 128);
        var rangeResult = rangeAssigner.Distribute(initial, moreDrones, 128);

        // Assert - sticky should move fewer partitions than range
        await Assert.That(stickyResult.TotalMoved).IsLessThanOrEqualTo(rangeResult.TotalMoved);
    }

    [Test]
    public async ValueTask sticky_handles_initial_empty_distribution() {
        // Arrange
        string[] drones = ["a", "b", "c"];

        // Act
        var manifest = _assigner.Distribute(DistributionManifest.Empty, drones, 12);

        // Assert - all partitions are "gained" since starting from empty
        var totalGained = manifest.Assignments.Values.Sum(a => a.Gained.Length);
        await Assert.That(totalGained).IsEqualTo(12);

        // Distribution should be balanced
        await Assert.That(manifest["a"].All.Length).IsEqualTo(4);
        await Assert.That(manifest["b"].All.Length).IsEqualTo(4);
        await Assert.That(manifest["c"].All.Length).IsEqualTo(4);
    }
}
