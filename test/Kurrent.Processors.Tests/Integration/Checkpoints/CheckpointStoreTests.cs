using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Integration tests for KurrentDBCheckpointStore against a real KurrentDB instance.
/// Tests direct Store/Load operations, concurrency, stream management, and edge cases.
/// </summary>
[Category("Processors")]
[Category("Integration")]
public class CheckpointStoreTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	KurrentDBCheckpointStore CreateStore(string processorId, int partitionCount = 8, int maxCount = 100) =>
        new(
            processorId,
            partitionCount,
            maxCount,
            Fixture.Client.Streams,
            Fixture.TypeMapper,
            TimeProvider.System,
            logger: TestkitExecutionContext.LoggerFactory.CreateLogger<KurrentDBCheckpointStore>()
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
		await store.StoreAsync("drone-1", barrierId, positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsGreaterThan(0);
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(1);
		await Assert.That(checkpoint.Positions[0].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(checkpoint.Timestamp).IsNotEqualTo(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask store_and_load_multiple_partitions_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-multi");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));

		// Act
		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);
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
		await store.StoreAsync("drone-1", PunyId.New(), positions1, ct);
		await store.StoreAsync("drone-1", PunyId.New(), positions2, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(2500);
		await Assert.That(byPartition[Partition.From(2)].LogPosition.Value).IsEqualTo(3000);
	}

	#endregion

	#region Concurrency

	[Test]
	public async ValueTask concurrent_stores_from_different_drones_merge_correctly(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-concurrent");
        var store1      = CreateStore(processorId);
        var store2      = CreateStore(processorId);
        var positions1  = CheckpointTestHelpers.Positions((0, 1000));
        var positions2  = CheckpointTestHelpers.Positions((1, 2000));

		// Act
		var task1 = store1.StoreAsync("drone-1", PunyId.New(), positions1, ct).AsTask();
		var task2 = store2.StoreAsync("drone-2", PunyId.New(), positions2, ct).AsTask();
		await Task.WhenAll(task1, task2);

		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(2000);
	}

	[Test]
	[Arguments(2)]
	[Arguments(5)]
	[Arguments(10)]
	public async ValueTask high_contention_stores_all_succeed(int droneCount, CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-contention");
		var stores = Enumerable.Range(0, droneCount)
			.Select(_ => CreateStore(processorId, partitionCount: droneCount))
			.ToArray();

		// Act
		var tasks = stores.Select((store, i) => {
			var positions = CheckpointTestHelpers.Positions((i, (i + 1) * 1000));
			return store.StoreAsync($"drone-{i}", PunyId.New(), positions, ct).AsTask();
		}).ToArray();

		await Task.WhenAll(tasks);

		var checkpoint = await stores[0].LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(droneCount);
		for (var i = 0; i < droneCount; i++) {
			await Assert
                .That(byPartition[Partition.From(i)].LogPosition.Value)
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
            .That(async () => await store.StoreAsync("drone-1", PunyId.New(), [], ct))
            .ThrowsExactly<ArgumentOutOfRangeException>();
	}

	[Test]
	public async ValueTask timestamp_increases_on_subsequent_stores(CancellationToken ct) {
		// Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-timestamp");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000));

		// Act
		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);
		var checkpoint1 = await store.LoadLatestAsync(ct);

		await Task.Delay(100, ct);

		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);
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
        var store       = CreateStore(processorId, maxCount: maxCount);
        var streamName  = $"$processors/{processorId}/checkpoints";

        // Act
		await store.StoreAsync("drone-1", PunyId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

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
		await store.StoreAsync("drone-1", PunyId.New(), CheckpointTestHelpers.Positions((0, 1000)), ct);

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

		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);

		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(3000);
		await Assert.That(byPartition[Partition.From(2)]).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });
	}

	[Test]
	public async ValueTask load_with_partitions_filters_to_requested_partitions_only(CancellationToken ct) {
		// Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("test-filter");
        var store       = CreateStore(processorId);
        var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000), (2, 3000));

        await store.StoreAsync("drone-1", PunyId.New(), positions, ct);

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
		await Assert.That(checkpoint.Positions[0]).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(0) });
		await Assert.That(checkpoint.Positions[1]).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(1) });
	}

	#endregion

	#region Reset

	[Test]
	public async ValueTask reset_with_empty_positions_acts_as_full_wipe(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-wipe");
		var store       = CreateStore(processorId);
		var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));

		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);

		// Act — full wipe (empty positions = barrier)
		await store.ResetAsync("drone-1", PunyId.New(), [], ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	[Test]
	public async ValueTask reset_with_positions_overrides_specified_partitions(CancellationToken ct) {
		// Arrange
		var processorId    = CheckpointTestHelpers.ProcessorId("test-selective-reset");
		var store          = CreateStore(processorId);
		var stream         = $"test-stream-{PunyId.New()}";
		var initialPos     = CheckpointTestHelpers.PositionsWithStream(
			(0, 1000, stream, 0), (1, 2000, stream, 1), (2, 3000, stream, 2)
		);
		var resetPositions = CheckpointTestHelpers.PositionsWithStream((1, 500, stream, 0));

		await store.StoreAsync("drone-1", PunyId.New(), initialPos, ct);

		// Act — selective reset: only partition 1 repositioned
		await store.ResetAsync("drone-1", PunyId.New(), resetPositions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partition 1 was reset, others preserved
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(500);
		await Assert.That(byPartition[Partition.From(2)].LogPosition.Value).IsEqualTo(3000);
	}

	[Test]
	public async ValueTask reset_with_unset_position_performs_full_wipe(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-unset-reset");
		var store       = CreateStore(processorId);
		var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));

		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);

		// Act — RecordPosition.Unset = full wipe
		await store.ResetAsync("drone-1", PunyId.New(), RecordPosition.Unset, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	[Test]
	public async ValueTask reset_with_position_repositions_all_partitions(CancellationToken ct) {
		// Arrange — seed a real event to get a valid log position for reset
		var processorId     = CheckpointTestHelpers.ProcessorId("test-pos-reset");
		var store           = CreateStore(processorId);
		var seeded          = await Fixture.SeedTestStream(ct);
		var resetToPosition = RecordPosition.From(seeded.Position.Value);
		var positions       = CheckpointTestHelpers.PositionsWithStream(
			(0, seeded.Position.Value + 100, seeded.Stream, 0),
			(1, seeded.Position.Value + 200, seeded.Stream, 1),
			(2, seeded.Position.Value + 300, seeded.Stream, 2)
		);

		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);

		// Act — reset all partitions to the seeded position
		await store.ResetAsync("drone-1", PunyId.New(), resetToPosition, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — all partitions at the reset position
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(seeded.Position.Value);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(seeded.Position.Value);
		await Assert.That(byPartition[Partition.From(2)].LogPosition.Value).IsEqualTo(seeded.Position.Value);
	}

	[Test]
	public async ValueTask full_wipe_acts_as_barrier_new_stores_after_wipe_are_visible(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-barrier");
		var store       = CreateStore(processorId);
		var oldPositions = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));
		var newPositions = CheckpointTestHelpers.Positions((0, 5000));

		await store.StoreAsync("drone-1", PunyId.New(), oldPositions, ct);

		// Act — wipe then store new data
		await store.ResetAsync("drone-1", PunyId.New(), [], ct);
		await store.StoreAsync("drone-1", PunyId.New(), newPositions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — only the post-wipe data is visible
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(1);
		await Assert.That(checkpoint.Positions[0].LogPosition.Value).IsEqualTo(5000);
	}

	#endregion

	#region Serialization

	[Test]
	public async ValueTask store_and_load_preserves_stream_and_revision(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("test-stream-rt");
		var store       = CreateStore(processorId);
		var streamName  = $"test-stream-{PunyId.New()}";
		var positions   = CheckpointTestHelpers.PositionsWithStream(
			(0, 1000, streamName, 5),
			(1, 2000, streamName, 10)
		);

		// Act
		await store.StoreAsync("drone-1", PunyId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		var p0 = byPartition[Partition.From(0)];
		var p1 = byPartition[Partition.From(1)];

		await Assert.That(p0.LogPosition.Value).IsEqualTo(1000);
		await Assert.That(p0.Stream).IsEqualTo(streamName);
		await Assert.That(p0.Revision).IsEqualTo(Revision.From(5));

		await Assert.That(p1.LogPosition.Value).IsEqualTo(2000);
		await Assert.That(p1.Stream).IsEqualTo(streamName);
		await Assert.That(p1.Revision).IsEqualTo(Revision.From(10));
	}

	[Test]
	public async ValueTask fresh_client_can_load_without_prior_store(CancellationToken ct) {
		// Arrange — store with one client instance
		var processorId = CheckpointTestHelpers.ProcessorId("test-fresh-read");
		var store1      = CreateStore(processorId);
		var positions   = CheckpointTestHelpers.Positions((0, 1000), (1, 2000));

		await store1.StoreAsync("drone-1", PunyId.New(), positions, ct);

		// Act — load with a fresh store instance (no prior write in this instance)
		var store2     = CreateStore(processorId);
		var checkpoint = await store2.LoadLatestAsync(ct);

		// Assert — types were registered via constructor, so deserialization works
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(1000);
		await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(2000);
	}

	#endregion
}
