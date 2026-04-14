using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Sdk.TestKit.Fixtures;
using CheckpointStore = Kurrent.Processors.Checkpointing.CheckpointStore;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Integration tests for CheckpointStore API against a real KurrentDB instance.
/// Tests direct Store/Load operations, concurrency, stream management, and edge cases.
/// </summary>
[Category("Processors")]
[Category("Integration")]
public class CheckpointStoreTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	CheckpointStore CreateStore(string processorId, int maxCount = 100) =>
        new(
            processorId,
            maxCount,
            Fixture.Client.Streams,
            TimeProvider.System,
            logger: TestkitExecutionContext.LoggerFactory.CreateLogger<CheckpointStore>()
        );

    #region Core Operations

	[Test]
	public async ValueTask load_returns_empty_checkpoint_when_no_checkpoint_exists(CancellationToken ct) {
		// Arrange
		var store = CreateStore(CheckpointTestHelpers.ProcessorId("test-empty"));

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(0);
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(0);
	}

	[Test]
	public async ValueTask store_and_load_single_partition_checkpoint(CancellationToken ct) {
		// Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-single");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000));
        var barrierId   = PunyId.New();

		// Act
		await store.StoreAsync("worker-1", barrierId, positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsGreaterThan(0);
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(1);
		await Assert.That(checkpoint.Positions[0].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Timestamp).IsNotEqualTo(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask store_and_load_multiple_partitions_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-multi");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));

		// Act
		await store.StoreAsync("worker-1", PunyId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(3);
		await Assert.That(checkpoint.LowWatermark.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask subsequent_store_merges_positions_with_existing(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-merge");
		var store       = CreateStore(processorId);
        var positions1  = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));
        var positions2  = CheckpointTestHelpers.Positions((1, 2500), (2, 3000));

		// Act
		await store.StoreAsync("worker-1", PunyId.New(), positions1, ct);
		await store.StoreAsync("worker-1", PunyId.New(), positions2, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2500);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(3000);
	}

	#endregion

	#region Concurrency

	[Test]
	public async ValueTask concurrent_stores_from_different_workers_merge_correctly(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-concurrent");
        var store1      = CreateStore(processorId);
        var store2      = CreateStore(processorId);
        var positions1  = CheckpointTestHelpers.Positions((0, 1000));
        var positions2  = CheckpointTestHelpers.Positions((1, 2000));

		// Act
		var task1 = store1.StoreAsync("worker-1", PunyId.New(), positions1, ct).AsTask();
		var task2 = store2.StoreAsync("worker-2", PunyId.New(), positions2, ct).AsTask();
		await Task.WhenAll(task1, task2);

		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(2000);
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

		// Act
		var tasks = stores.Select((store, i) => {
			var positions = CheckpointTestHelpers.Positions((i, (i + 1) * 1000));
			return store.StoreAsync($"worker-{i}", PunyId.New(), positions, ct).AsTask();
		}).ToArray();

		await Task.WhenAll(tasks);

		var checkpoint = await stores[0].LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(workerCount);
		for (var i = 0; i < workerCount; i++) {
			await Assert
                .That(byPartition[Partition.From(i)].Position.LogPosition.Value)
                .IsEqualTo((i + 1) * 1000);
		}
	}

	#endregion

	#region Edge Cases

	[Test]
	public async ValueTask store_with_empty_positions_throws(CancellationToken ct) {
        // Arrange
        var store = CreateStore(CheckpointTestHelpers.ProcessorId("test-empty-pos"));

        // Act & Assert
		await Assert
            .That(async () => await store.StoreAsync("worker-1", PunyId.New(), [], ct))
            .ThrowsExactly<ArgumentOutOfRangeException>();
	}

	[Test]
	public async ValueTask timestamp_increases_on_subsequent_stores(CancellationToken ct) {
		// Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-timestamp");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000));

		// Act
		await store.StoreAsync("worker-1", PunyId.New(), positions, ct);
		var checkpoint1 = await store.LoadLatestAsync(ct);

		await Task.Delay(100, ct);

		await store.StoreAsync("worker-1", PunyId.New(), positions, ct);
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
        var maxCount    = 50;
        var store       = CreateStore(processorId, maxCount);
        var streamName  = $"$processors/{processorId}/checkpoints";

        // Act
		await store.StoreAsync("worker-1", PunyId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Assert
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
		await Assert.That(info.Retention.HasMaxCount).IsTrue();
		await Assert.That(info.Retention.MaxCount).IsEqualTo(maxCount);
	}

	[Test]
	public async ValueTask maxCount_zero_skips_retention_configuration(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-no-retention");
        var store       = CreateStore(processorId, maxCount: 0);
        var streamName  = $"$processors/{processorId}/checkpoints";

        // Act
		await store.StoreAsync("worker-1", PunyId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

		// Assert
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
		await Assert.That(info.Retention.HasMaxCount).IsFalse();
	}

	#endregion

	#region Checkpoint Resolution

	[Test]
	public async ValueTask load_with_partitions_resolves_positions_and_computes_watermarks(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-watermarks");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 3000));

		await store.StoreAsync("worker-1", PunyId.New(), positions, ct);

		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(3000);
		await Assert.That(byPartition[Partition.From(2)].Position).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });
	}

	[Test]
	public async ValueTask load_with_partitions_filters_to_requested_partitions_only(CancellationToken ct) {
		// Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-filter");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));

        await store.StoreAsync("worker-1", PunyId.New(), positions, ct);

		var partitions = new[] { Partition.From(0), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert
		var partitionKeys = checkpoint.Positions.Select(p => p.Partition).ToArray();
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(2);
		await Assert.That(partitionKeys).Contains(Partition.From(0));
		await Assert.That(partitionKeys).Contains(Partition.From(2));
		await Assert.That(partitionKeys).DoesNotContain(Partition.From(1));
	}

	[Test]
	public async ValueTask load_with_partitions_uses_initial_position_for_missing_partitions(CancellationToken ct) {
        // Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-initial");
        var store       = CreateStore(processorId);

        var partitions = new[] { Partition.From(0), Partition.From(1) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Latest, ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(2);
		await Assert.That(checkpoint.Positions[0].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(0) });
		await Assert.That(checkpoint.Positions[1].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(1) });
	}

	#endregion

	#region Properties

	[Test]
	public async ValueTask processor_id_property_matches_constructor_argument(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-prop");
        var store       = CreateStore(processorId);

        // Assert
		await Assert.That(store.ProcessorId).IsEqualTo(processorId);
	}

	#endregion
}
