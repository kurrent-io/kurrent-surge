using System.Collections.Immutable;
using Kurrent.Client.Processors.Checkpointing;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// Integration tests for CheckpointStore API against a real KurrentDB instance.
/// Tests direct Store/Load operations, concurrency, stream management, and edge cases.
/// </summary>
[Category("Processors")]
[Category("Checkpoint")]
[Category("Integration")]
public class CheckpointStoreTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	CheckpointStore CreateStore(string processorId, int maxCount = 100) {
		return new CheckpointStore(
			processorId,
			maxCount,
			Fixture.Client.Streams,
			TimeProvider.System,
			logger: null
		);
	}

	#region Core Operations

	[Test]
	public async ValueTask load_returns_empty_checkpoint_when_no_checkpoint_exists(CancellationToken ct) {
		// Arrange
		var store = CreateStore(CheckpointTestHelpers.ProcessorId("test-empty"));

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
		await Assert.That(checkpoint.HasCheckpoint).IsFalse();
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(0);
		await Assert.That(checkpoint.BarrierId).IsEqualTo(BarrierId.Empty);
	}

	[Test]
	public async ValueTask store_and_load_single_partition_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-single");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000));
		var barrierId = BarrierId.New();

		// Act - Store
		await store.StoreAsync("worker-1", barrierId, positions, ct);

		// Act - Load
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.HasCheckpoint).IsTrue();
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(1);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].WorkerId).IsEqualTo("worker-1");
		await Assert.That(checkpoint.BarrierId).IsEqualTo(barrierId);
		await Assert.That(checkpoint.Timestamp).IsNotEqualTo(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask store_and_load_multiple_partitions_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-multi");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));
		var barrierId = BarrierId.New();

		// Act
		await store.StoreAsync("worker-1", barrierId, positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert - partition positions
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2000);
		await Assert.That(checkpoint.Partitions[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(3000);

		// Assert - watermarks computed from stored partitions
		await Assert.That(checkpoint.LowWatermark.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask subsequent_store_merges_positions_with_existing(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-merge");
		var store = CreateStore(processorId);
		var positions1 = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));
		var positions2 = CheckpointTestHelpers.Positions((1, 2500), (2, 3000)); // Update partition 1, add partition 2

		// Act
		await store.StoreAsync("worker-1", BarrierId.New(), positions1, ct);
		await store.StoreAsync("worker-1", BarrierId.New(), positions2, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000); // Retained
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2500); // Updated
		await Assert.That(checkpoint.Partitions[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(3000); // Added
	}

	#endregion

	#region Concurrency

	[Test]
	public async ValueTask concurrent_stores_from_different_workers_merge_correctly(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-concurrent");
		var store1 = CreateStore(processorId);
		var store2 = CreateStore(processorId);
		var positions1 = CheckpointTestHelpers.Positions((0, 1000));
		var positions2 = CheckpointTestHelpers.Positions((1, 2000));

		// Act - Simulate near-simultaneous stores from 2 workers
		var task1 = store1.StoreAsync("worker-1", BarrierId.New(), positions1, ct).AsTask();
		var task2 = store2.StoreAsync("worker-2", BarrierId.New(), positions2, ct).AsTask();
		await Task.WhenAll(task1, task2);

		// Load final state
		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert - Both workers' positions should be present
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(2);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2000);
	}

	[Test]
	[Arguments(2)]
	[Arguments(5)]
	[Arguments(10)]
	public async ValueTask high_contention_stores_all_succeed(int workerCount, CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-contention");
		var stores = Enumerable.Range(0, workerCount)
			.Select(_ => CreateStore(processorId))
			.ToArray();

		// Act - All workers store to same processor simultaneously
		var tasks = stores.Select((store, i) => {
			var positions = CheckpointTestHelpers.Positions((i, (i + 1) * 1000));
			return store.StoreAsync($"worker-{i}", BarrierId.New(), positions, ct).AsTask();
		}).ToArray();

		await Task.WhenAll(tasks);

		// Load final state
		var checkpoint = await stores[0].LoadLatestAsync(ct);

		// Assert - All partitions should be present
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(workerCount);
		for (var i = 0; i < workerCount; i++) {
			await Assert.That(checkpoint.Partitions[Partition.From(i)].Position.LogPosition.Value)
				.IsEqualTo((i + 1) * 1000);
		}
	}

	#endregion

	#region Edge Cases

	[Test]
	public async ValueTask store_with_empty_positions_throws_invalid_operation(CancellationToken ct) {
		// Arrange
		var store = CreateStore(CheckpointTestHelpers.ProcessorId("test-empty-pos"));
		var emptyPositions = ImmutableDictionary<Partition, RecordPosition>.Empty;

		// Act & Assert
		await Assert.That(async () => {
			await store.StoreAsync("worker-1", BarrierId.New(), emptyPositions, ct);
		}).ThrowsExactly<InvalidOperationException>();
	}

	[Test]
	public async ValueTask barrier_id_updated_on_each_store(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000));
		var barrier1 = BarrierId.New();
		var barrier2 = BarrierId.New();

		// Act
		await store.StoreAsync("worker-1", barrier1, positions, ct);
		var checkpoint1 = await store.LoadLatestAsync(ct);

		await store.StoreAsync("worker-1", barrier2, positions, ct);
		var checkpoint2 = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint1.BarrierId).IsEqualTo(barrier1);
		await Assert.That(checkpoint2.BarrierId).IsEqualTo(barrier2);
		await Assert.That(checkpoint2.BarrierId).IsNotEqualTo(barrier1);
	}

	[Test]
	public async ValueTask timestamp_increases_on_subsequent_stores(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-timestamp");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000));

		// Act
		await store.StoreAsync("worker-1", BarrierId.New(), positions, ct);
		var checkpoint1 = await store.LoadLatestAsync(ct);

		await Task.Delay(100, ct); // Ensure time passes

		await store.StoreAsync("worker-1", BarrierId.New(), positions, ct);
		var checkpoint2 = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint2.Timestamp).IsGreaterThan(checkpoint1.Timestamp);
	}

	#endregion

	#region Stream Management

	[Test]
	public async ValueTask ensures_stream_retention_policy_on_first_store(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-retention");
		var maxCount = 50;
		var store = CreateStore(processorId, maxCount);
		var streamName = $"$processors/{processorId}/checkpoints";

		// Act
		await store.StoreAsync("worker-1", BarrierId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Get stream info to verify retention policy
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();

		// Assert
		await Assert.That(info.Metadata.HasMaxCount).IsTrue();
		await Assert.That(info.Metadata.MaxCount).IsEqualTo(maxCount);
	}

	[Test]
	public async ValueTask does_not_overwrite_existing_retention_policy(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-no-overwrite");
		var streamName = $"$processors/{processorId}/checkpoints";

		// Pre-configure stream with different retention
		await Fixture.Client.Streams
			.ConfigureStreamDataRetention(streamName, new() { MaxCount = 10 }, ct)
			.UnwrapAsync();

		// Act - Create store with different maxCount
		var store = CreateStore(processorId, maxCount: 100);
		await store.StoreAsync("worker-1", BarrierId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Get stream info
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();

		// Assert - Original retention should be preserved
		await Assert.That(info.Metadata.MaxCount).IsEqualTo(10);
	}

	[Test]
	public async ValueTask maxCount_zero_skips_retention_configuration(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-no-retention");
		var store = CreateStore(processorId, maxCount: 0);
		var streamName = $"$processors/{processorId}/checkpoints";

		// Act
		await store.StoreAsync("worker-1", BarrierId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Get stream info
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct).UnwrapAsync();

		// Assert - No retention policy should be set
		await Assert.That(info.Metadata.HasMaxCount).IsFalse();
	}

	#endregion

	#region Checkpoint Resolution

	[Test]
	public async ValueTask load_with_partitions_resolves_positions_and_computes_watermarks(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-watermarks");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 3000));

		await store.StoreAsync("worker-1", BarrierId.New(), positions, ct);

		// Request partitions 0, 1 (checkpointed), and 2 (new, needs default)
		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert - resolved positions
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(3000);
		await Assert.That(checkpoint.Partitions[Partition.From(2)].Position).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });

		// Assert - watermarks
		await Assert.That(checkpoint.LowWatermark).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask load_with_partitions_filters_to_requested_partitions_only(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-filter");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));

		await store.StoreAsync("worker-1", BarrierId.New(), positions, ct);

		// Request only partitions 0 and 2
		var partitions = new[] { Partition.From(0), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert - only requested partitions returned
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(2);
		await Assert.That(checkpoint.Partitions.ContainsKey(Partition.From(0))).IsTrue();
		await Assert.That(checkpoint.Partitions.ContainsKey(Partition.From(2))).IsTrue();
		await Assert.That(checkpoint.Partitions.ContainsKey(Partition.From(1))).IsFalse();

		// Assert - watermarks computed from filtered partitions only
		await Assert.That(checkpoint.LowWatermark.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask load_with_partitions_uses_initial_position_for_missing_partitions(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-initial");
		var store = CreateStore(processorId);

		// No checkpoint stored - request new partitions
		var partitions = new[] { Partition.From(0), Partition.From(1) };

		// Act - with Latest as initial position
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Latest, ct);

		// Assert - all partitions get Latest position
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(2);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(0) });
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(1) });
		await Assert.That(checkpoint.LowWatermark).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(0) });
	}

	#endregion

	#region Load By Barrier

	[Test]
	public async ValueTask load_by_barrier_returns_empty_when_barrier_not_found(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier-notfound");
		var store = CreateStore(processorId);

		// Store with one barrier
		await store.StoreAsync("worker-1", BarrierId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Act - try to load with different barrier
		var checkpoint = await store.LoadAsync(BarrierId.New(), ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	[Test]
	public async ValueTask load_by_barrier_returns_checkpoint_with_watermarks(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier-load");
		var store = CreateStore(processorId);
		var barrierId = BarrierId.New();
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 3000), (2, 2000));

		await store.StoreAsync("worker-1", barrierId, positions, ct);

		// Act
		var checkpoint = await store.LoadAsync(barrierId, ct);

		// Assert - positions
		await Assert.That(checkpoint.HasCheckpoint).IsTrue();
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.BarrierId).IsEqualTo(barrierId);

		// Assert - watermarks computed from stored partitions
		await Assert.That(checkpoint.LowWatermark.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask load_by_barrier_with_partitions_resolves_and_computes_watermarks(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier-partitions");
		var store = CreateStore(processorId);
		var barrierId = BarrierId.New();
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 3000));

		await store.StoreAsync("worker-1", barrierId, positions, ct);

		// Request partitions 0, 1 (checkpointed), and 2 (new, needs default)
		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadAsync(barrierId, partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert - resolved positions
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(3000);
		await Assert.That(checkpoint.Partitions[Partition.From(2)].Position).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });

		// Assert - watermarks (low is Earliest because partition 2 has no checkpoint)
		await Assert.That(checkpoint.LowWatermark).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask load_by_barrier_finds_older_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier-older");
		var store = CreateStore(processorId);
		var barrier1 = BarrierId.New();
		var barrier2 = BarrierId.New();

		// Store two checkpoints
		await store.StoreAsync("worker-1", barrier1, CheckpointTestHelpers.Positions((0, 1000)), ct);
		await store.StoreAsync("worker-1", barrier2, CheckpointTestHelpers.Positions((0, 2000)), ct);

		// Act - load the older checkpoint
		var checkpoint = await store.LoadAsync(barrier1, ct);

		// Assert - should get the first checkpoint
		await Assert.That(checkpoint.BarrierId).IsEqualTo(barrier1);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
	}

	#endregion

	#region Properties

	[Test]
	public async ValueTask processor_id_property_matches_constructor_argument(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-prop");
		var store = CreateStore(processorId);

		// Assert
		await Assert.That(store.ProcessorId).IsEqualTo(processorId);
	}

	#endregion
}
