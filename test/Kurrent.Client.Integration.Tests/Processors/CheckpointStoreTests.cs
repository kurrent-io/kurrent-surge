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

		// Assert
		await Assert.That(checkpoint.Partitions.Count).IsEqualTo(3);
		await Assert.That(checkpoint.Partitions[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Partitions[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2000);
		await Assert.That(checkpoint.Partitions[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(3000);
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
	public async ValueTask resolves_start_position_with_partition_override(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-override");
		var store = CreateStore(processorId);
		var positions = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));

		await store.StoreAsync("worker-1", BarrierId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Define override for partition 1
		var overrides = ImmutableDictionary<int, long>.Empty.Add(1, 5000);

		// Act
		var (startFrom, resolvedPositions) = checkpoint.ResolveStartPosition(
			SubscriptionInitialPosition.Earliest,
			overrides
		);

		// Assert
		await Assert.That(resolvedPositions[Partition.From(0)].LogPosition.Value).IsEqualTo(1000); // From checkpoint
		await Assert.That(resolvedPositions[Partition.From(1)].LogPosition.Value).IsEqualTo(5000); // Overridden
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
