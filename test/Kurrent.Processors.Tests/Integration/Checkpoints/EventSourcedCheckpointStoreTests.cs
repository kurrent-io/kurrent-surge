using Bogus;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Models;
using Kurrent.Testing.Testkit;
using Microsoft.Extensions.Logging;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Processors.Tests;

/// <summary>
/// Integration tests for EventSourcedCheckpointStore API against a real KurrentDB instance.
/// </summary>
[Category("Processors")]
[Category("Integration")]
public class EventSourcedCheckpointStoreTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
	public required BogusFaker Faker { get; [UsedImplicitly] init; }

	EventSourcedCheckpointStore CreateStore(string processorId, int partitionCount = 8, int maxCount = 1000) =>
		new(
			processorId,
			partitionCount,
			maxCount,
			Fixture.Client.Streams,
			TimeProvider.System,
			logger: TestkitExecutionContext.LoggerFactory.CreateLogger<EventSourcedCheckpointStore>()
		);

	string WorkerId() => Faker.Internet.UserName();

	string Stream() => $"{Faker.Commerce.Department()}-{Faker.Random.AlphaNumeric(8)}";

	long Pos() => Faker.Random.Long(1, 100_000);

	int Rev() => Faker.Random.Int(0, 10_000);

	PartitionPosition[] Positions(params (int partition, long position, string stream, int revision)[] pairs) =>
		pairs.Select(p => new PartitionPosition(
			Partition.From(p.partition),
			RecordPosition.From(p.position, p.stream, Revision.From(p.revision), Partition.From(p.partition))
		)).ToArray();

	(long pos, string stream, int rev) PosData() => (Pos(), Stream(), Rev());

	#region Core Operations

	[Test]
	public async ValueTask load_returns_empty_checkpoint_when_no_checkpoint_exists(CancellationToken ct) {
		// Arrange
		var store = CreateStore(CheckpointTestHelpers.ProcessorId("es-empty"));

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
		var processorId            = CheckpointTestHelpers.ProcessorId("es-single");
		var store                  = CreateStore(processorId, partitionCount: 1);
		var workerId               = WorkerId();
		var (pos, stream, rev)     = PosData();
		var positions              = Positions((0, pos, stream, rev));
		var barrierId              = PunyId.New();

		// Act
		await store.StoreAsync(workerId, barrierId, positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsGreaterThan(0);
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(1);
		await Assert.That(checkpoint.Positions[0].Partition).IsEqualTo(Partition.From(0));
		await Assert.That(checkpoint.Positions[0].Position.LogPosition.Value).IsEqualTo(pos);
		await Assert.That(checkpoint.Positions[0].Position.Stream.ToString()).IsEqualTo(stream);
		await Assert.That(checkpoint.Positions[0].Position.Revision).IsEqualTo(Revision.From(rev));
		await Assert.That(checkpoint.Timestamp).IsNotEqualTo(DateTimeOffset.MinValue);
	}

	[Test]
	public async ValueTask store_and_load_multiple_partitions_checkpoint(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-multi");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (p0, s0, r0)        = PosData();
		var (_, s1, r1)         = PosData();
		var (_, s2, r2)         = PosData();
		var pos0                = p0;
		var pos1                = pos0 + Faker.Random.Long(1, 1000);
		var pos2                = pos1 + Faker.Random.Long(1, 1000);
		var positions            = Positions((0, pos0, s0, r0), (1, pos1, s1, r1), (2, pos2, s2, r2));

		// Act
		await store.StoreAsync(workerId, PunyId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(3);
		await Assert.That(checkpoint.LowWatermark.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(checkpoint.HighWatermark.LogPosition.Value).IsEqualTo(pos2);
	}

	[Test]
	public async ValueTask subsequent_store_merges_positions_with_existing(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-merge");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (pos0, s0, r0)      = PosData();
		var (pos1, s1, r1)      = PosData();
		var pos1Updated          = pos1 + Faker.Random.Long(1, 1000);
		var (_, s1u, r1u)       = PosData();
		var (pos2, s2, r2)      = PosData();

		// Act
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1)), ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions((1, pos1Updated, s1u, r1u), (2, pos2, s2, r2)), ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — 3 partitions: 0 retained, 1 updated, 2 added
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(3);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1Updated);
		await Assert.That(byPartition[Partition.From(1)].Position.Stream.ToString()).IsEqualTo(s1u);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(pos2);
	}

	#endregion

	#region Concurrency

	[Test]
	public async ValueTask concurrent_stores_from_different_workers_merge_correctly(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-concurrent");
		var store1               = CreateStore(processorId, partitionCount: 2);
		var store2               = CreateStore(processorId, partitionCount: 2);
		var (pos0, s0, r0)      = PosData();
		var (pos1, s1, r1)      = PosData();

		// Act — two workers append simultaneously
		var task1 = store1.StoreAsync(WorkerId(), PunyId.New(), Positions((0, pos0, s0, r0)), ct).AsTask();
		var task2 = store2.StoreAsync(WorkerId(), PunyId.New(), Positions((1, pos1, s1, r1)), ct).AsTask();
		await Task.WhenAll(task1, task2);

		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1);
	}

	[Test]
	[Arguments(2)]
	[Arguments(5)]
	[Arguments(10)]
	public async ValueTask high_contention_stores_all_succeed(int workerCount, CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-contention");
		var stores = Enumerable.Range(0, workerCount)
			.Select(_ => CreateStore(processorId, partitionCount: workerCount))
			.ToArray();

		var expectedData = Enumerable.Range(0, workerCount).Select(_ => PosData()).ToArray();

		// Act — all workers store simultaneously
		var tasks = stores.Select((store, i) =>
			store.StoreAsync(WorkerId(), PunyId.New(), Positions((i, expectedData[i].pos, expectedData[i].stream, expectedData[i].rev)), ct).AsTask()
		).ToArray();

		await Task.WhenAll(tasks);

		var checkpoint = await stores[0].LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(workerCount);
		for (var i = 0; i < workerCount; i++) {
			await Assert
				.That(byPartition[Partition.From(i)].Position.LogPosition.Value)
				.IsEqualTo(expectedData[i].pos);
		}
	}

	#endregion

	#region Edge Cases

	[Test]
	public async ValueTask store_with_empty_positions_throws(CancellationToken ct) {
		// Arrange
		var store = CreateStore(CheckpointTestHelpers.ProcessorId("es-empty-pos"));

		// Act & Assert
		await Assert
			.That(async () => await store.StoreAsync(WorkerId(), PunyId.New(), [], ct))
			.ThrowsExactly<ArgumentOutOfRangeException>();
	}

	[Test]
	public async ValueTask store_with_null_worker_id_throws(CancellationToken ct) {
		// Arrange
		var store          = CreateStore(CheckpointTestHelpers.ProcessorId("es-null-worker"));
		var (p, s, r)     = PosData();

		// Act & Assert
		await Assert
			.That(async () => await store.StoreAsync(null!, PunyId.New(), Positions((0, p, s, r)), ct))
			.ThrowsExactly<ArgumentNullException>();
	}

	[Test]
	public async ValueTask store_with_empty_checkpoint_id_throws(CancellationToken ct) {
		// Arrange
		var store          = CreateStore(CheckpointTestHelpers.ProcessorId("es-null-ckpt"));
		var (p, s, r)     = PosData();

		// Act & Assert
		await Assert
			.That(async () => await store.StoreAsync(WorkerId(), string.Empty, Positions((0, p, s, r)), ct))
			.ThrowsExactly<ArgumentException>();
	}

	[Test]
	public async ValueTask timestamp_increases_on_subsequent_stores(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-timestamp");
		var store       = CreateStore(processorId, partitionCount: 1);
		var workerId    = WorkerId();

		// Act
		var (p1, s1, r1) = PosData();
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p1, s1, r1)), ct);
		var checkpoint1 = await store.LoadLatestAsync(ct);

		await Task.Delay(100, ct);

		var (p2, s2, r2) = PosData();
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p2, s2, r2)), ct);
		var checkpoint2 = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint2.Timestamp).IsGreaterThan(checkpoint1.Timestamp);
	}

	#endregion

	#region Stream Management

	[Test]
	public async ValueTask ensures_stream_retention_policy_on_first_store(CancellationToken ct) {
		// Arrange
		var processorId       = CheckpointTestHelpers.ProcessorId("es-retention");
		var partitionCount    = Faker.Random.Int(2, 8);
		var maxCount          = Faker.Random.Int(100, 500);
		var store             = CreateStore(processorId, partitionCount, maxCount);
		var streamName        = $"$processors/{processorId}/checkpoints";
		var (p, s, r)        = PosData();

		// Act
		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, p, s, r)), ct);

		// Assert
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
		await Assert.That(info.Retention.HasMaxCount).IsTrue();
		await Assert.That(info.Retention.MaxCount).IsEqualTo(maxCount);
	}

	[Test]
	public async ValueTask enforces_configured_retention_over_existing(CancellationToken ct) {
		// Arrange
		var processorId      = CheckpointTestHelpers.ProcessorId("es-enforce-ret");
		var streamName       = $"$processors/{processorId}/checkpoints";
		var existingMaxCount = Faker.Random.Int(5, 15);
		var storeMaxCount    = Faker.Random.Int(500, 1500);
		var (p, s, r)       = PosData();

		// Pre-configure stream with different retention
		await Fixture.Client.Streams.ConfigureDataRetention(streamName, maxCount: existingMaxCount, ct);

		// Act — create store with its own maxCount
		var store = CreateStore(processorId, partitionCount: 4, maxCount: storeMaxCount);
		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, p, s, r)), ct);

		// Assert — store's retention enforced
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
		await Assert.That(info.Retention.MaxCount).IsEqualTo(storeMaxCount);
	}

	[Test]
	public async ValueTask maxCount_zero_skips_retention_configuration(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-no-retention");
		var store       = CreateStore(processorId, partitionCount: 4, maxCount: 0);
		var streamName  = $"$processors/{processorId}/checkpoints";
		var (p, s, r)  = PosData();

		// Act
		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, p, s, r)), ct);

		// Assert
		var info = await Fixture.Client.Streams.GetStreamInfo(streamName, ct);
		await Assert.That(info.Retention.HasMaxCount).IsFalse();
	}

	#endregion

	#region Checkpoint Resolution

	[Test]
	public async ValueTask load_with_partitions_resolves_positions_and_computes_watermarks(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-watermarks");
		var store                = CreateStore(processorId, partitionCount: 3);
		var (p0, s0, r0)       = PosData();
		var pos0                = p0;
		var pos1                = pos0 + Faker.Random.Long(1, 1000);
		var (_, s1, r1)        = PosData();

		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1)), ct);

		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(3);

		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1);
		await Assert.That(byPartition[Partition.From(2)].Position).IsEqualTo(RecordPosition.Earliest with { Partition = Partition.From(2) });
	}

	[Test]
	public async ValueTask load_with_partitions_filters_to_requested_partitions_only(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-filter");
		var store                = CreateStore(processorId, partitionCount: 3);
		var (p0, s0, r0)       = PosData();
		var (p1, s1, r1)       = PosData();
		var (p2, s2, r2)       = PosData();

		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, p0, s0, r0), (1, p1, s1, r1), (2, p2, s2, r2)), ct);

		var partitions = new[] { Partition.From(0), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert — only requested partitions
		var partitionKeys = checkpoint.Positions.Select(p => p.Partition).ToArray();
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(2);
		await Assert.That(partitionKeys).Contains(Partition.From(0));
		await Assert.That(partitionKeys).Contains(Partition.From(2));
		await Assert.That(partitionKeys).DoesNotContain(Partition.From(1));
	}

	[Test]
	public async ValueTask load_with_partitions_uses_initial_position_for_missing_partitions(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-initial");
		var store       = CreateStore(processorId, partitionCount: 2);
		var partitions  = new[] { Partition.From(0), Partition.From(1) };

		// Act — no checkpoint stored, Latest as initial position
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Latest, ct);

		// Assert
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(2);
		await Assert.That(checkpoint.Positions[0].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(0) });
		await Assert.That(checkpoint.Positions[1].Position).IsEqualTo(RecordPosition.Latest with { Partition = Partition.From(1) });
	}

	[Test]
	public async ValueTask load_with_empty_partitions_returns_empty(CancellationToken ct) {
		// Arrange
		var store          = CreateStore(CheckpointTestHelpers.ProcessorId("es-empty-parts"), partitionCount: 2);
		var (p, s, r)     = PosData();
		await store.StoreAsync(WorkerId(), PunyId.New(), Positions((0, p, s, r)), ct);

		// Act
		var checkpoint = await store.LoadLatestAsync([], SubscriptionInitialPosition.Earliest, ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	#endregion

	#region Reset — Full

	[Test]
	public async ValueTask full_reset_wipes_all_partitions(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-full-reset");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (p0, s0, r0)       = PosData();
		var (p1, s1, r1)       = PosData();
		var (p2, s2, r2)       = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p0, s0, r0), (1, p1, s1, r1), (2, p2, s2, r2)), ct);

		// Act — full reset (empty positions)
		await store.ResetAsync(workerId, PunyId.New(), [], ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — everything wiped
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	[Test]
	public async ValueTask store_after_full_reset_starts_fresh(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-post-full-reset");
		var store                = CreateStore(processorId, partitionCount: 4);
		var workerId             = WorkerId();
		var (pos0, s0, r0)     = PosData();
		var (pos1, s1, r1)     = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev),
			(2, PosData().pos, PosData().stream, PosData().rev),
			(3, PosData().pos, PosData().stream, PosData().rev)
		), ct);
		await store.ResetAsync(workerId, PunyId.New(), [], ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1)), ct);

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — only partitions from new store, pre-reset data gone
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1);
	}

	[Test]
	public async ValueTask full_reset_with_partitions_resolves_all_to_initial_position(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-full-reset-resolve");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (p0, s0, r0)       = PosData();
		var (p1, s1, r1)       = PosData();
		var (p2, s2, r2)       = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p0, s0, r0), (1, p1, s1, r1), (2, p2, s2, r2)), ct);
		await store.ResetAsync(workerId, PunyId.New(), [], ct);

		var partitions = new[] { Partition.From(0), Partition.From(1), Partition.From(2) };

		// Act
		var checkpoint = await store.LoadLatestAsync(partitions, SubscriptionInitialPosition.Earliest, ct);

		// Assert — all partitions resolve to Earliest
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(3);
		foreach (var pos in checkpoint.Positions)
			await Assert.That(pos.Position.LogPosition).IsEqualTo(LogPosition.Earliest);
	}

	[Test]
	public async ValueTask multiple_full_resets_only_latest_matters(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-multi-reset");
		var store                = CreateStore(processorId, partitionCount: 2);
		var workerId             = WorkerId();
		var (pos0, s0, r0)     = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev)
		), ct);
		await store.ResetAsync(workerId, PunyId.New(), [], ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev)
		), ct);
		await store.ResetAsync(workerId, PunyId.New(), [], ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, pos0, s0, r0)), ct);

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — only the last store after the last reset
		await Assert.That(checkpoint.Positions.Length).IsEqualTo(1);
		await Assert.That(checkpoint.Positions[0].Position.LogPosition.Value).IsEqualTo(pos0);
	}

	#endregion

	#region Reset — Selective

	[Test]
	public async ValueTask selective_reset_repositions_specified_partitions(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-sel-reset");
		var store                    = CreateStore(processorId, partitionCount: 4);
		var workerId                 = WorkerId();
		var (pos0, s0, r0)         = PosData();
		var (pos1, s1, r1)         = PosData();
		var (pos2, s2, r2)         = PosData();
		var (pos3, s3, r3)         = PosData();
		var (resetPos1, rs1, rr1)  = PosData();
		var (resetPos3, rs3, rr3)  = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, pos0, s0, r0), (1, pos1, s1, r1), (2, pos2, s2, r2), (3, pos3, s3, r3)
		), ct);

		// Act — reset partitions 1 and 3 to new positions
		await store.ResetAsync(workerId, PunyId.New(), Positions((1, resetPos1, rs1, rr1), (3, resetPos3, rs3, rr3)), ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partitions 0,2 unchanged; partitions 1,3 repositioned
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(4);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(resetPos1);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(pos2);
		await Assert.That(byPartition[Partition.From(3)].Position.LogPosition.Value).IsEqualTo(resetPos3);
	}

	[Test]
	public async ValueTask selective_reset_to_earliest(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-sel-earliest");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (pos0, s0, r0)     = PosData();
		var (pos1, s1, r1)     = PosData();
		var (pos2, s2, r2)     = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1), (2, pos2, s2, r2)), ct);

		// Act — reset partition 1 to Earliest (resolved to first record in log via InspectRecord)
		await store.ResetAsync(workerId, PunyId.New(), [
			new(Partition.From(1), RecordPosition.Earliest)
		], ct);

		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partition 1 resolved to the first record in the log with full position data
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsGreaterThanOrEqualTo(0);
		await Assert.That(byPartition[Partition.From(1)].Position.Stream.IsUnset).IsFalse();
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition).IsEqualTo(pos2);
	}

	[Test]
	public async ValueTask selective_reset_then_store_updates_reset_partitions(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-sel-then-store");
		var store                    = CreateStore(processorId, partitionCount: 3);
		var workerId                 = WorkerId();
		var (pos0, s0, r0)         = PosData();
		var (pos1, s1, r1)         = PosData();
		var (pos2, s2, r2)         = PosData();
		var (resetPos1, rs1, rr1)  = PosData();
		var newPos1                 = resetPos1 + Faker.Random.Long(1, 5000);
		var (_, ns1, nr1)          = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1), (2, pos2, s2, r2)), ct);
		await store.ResetAsync(workerId, PunyId.New(), Positions((1, resetPos1, rs1, rr1)), ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions((1, newPos1, ns1, nr1)), ct);

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partition 1 has the latest store value, not the reset value
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(newPos1);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(pos2);
	}

	[Test]
	public async ValueTask selective_reset_from_different_worker(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-sel-diff-worker");
		var store                    = CreateStore(processorId, partitionCount: 4);
		var worker1                  = WorkerId();
		var worker2                  = WorkerId();
		var (pos0, s0, r0)         = PosData();
		var (pos1, s1, r1)         = PosData();
		var (pos2, s2, r2)         = PosData();
		var (pos3, s3, r3)         = PosData();
		var (resetPos2, rs2, rr2)  = PosData();

		await store.StoreAsync(worker1, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1)), ct);
		await store.StoreAsync(worker2, PunyId.New(), Positions((2, pos2, s2, r2), (3, pos3, s3, r3)), ct);

		// Act — worker1 resets partition 2 (which worker2 owns)
		await store.ResetAsync(worker1, PunyId.New(), Positions((2, resetPos2, rs2, rr2)), ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partition 2 repositioned, everything else intact
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(4);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(resetPos2);
		await Assert.That(byPartition[Partition.From(3)].Position.LogPosition.Value).IsEqualTo(pos3);
	}

	[Test]
	public async ValueTask full_reset_after_selective_reset_wipes_everything(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-full-after-sel");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (p0, s0, r0)       = PosData();
		var (p1, s1, r1)       = PosData();
		var (p2, s2, r2)       = PosData();
		var (rp1, rs1, rr1)    = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p0, s0, r0), (1, p1, s1, r1), (2, p2, s2, r2)), ct);
		await store.ResetAsync(workerId, PunyId.New(), Positions((1, rp1, rs1, rr1)), ct);
		await store.ResetAsync(workerId, PunyId.New(), [], ct);

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	#endregion

	#region Reset — All to Position

	[Test]
	public async ValueTask reset_all_to_position_overrides_existing_partitions(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-reset-all");
		var store                    = CreateStore(processorId, partitionCount: 4);
		var workerId                 = WorkerId();
		var (resetPos, rs, rr)     = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev),
			(2, PosData().pos, PosData().stream, PosData().rev),
			(3, PosData().pos, PosData().stream, PosData().rev)
		), ct);

		// Act
		await store.ResetAsync(workerId, PunyId.New(), RecordPosition.From(resetPos, rs, Revision.From(rr)), ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — all 4 existing partitions at the reset position
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(4);
		for (var i = 0; i < 4; i++)
			await Assert.That(byPartition[Partition.From(i)].Position.LogPosition.Value).IsEqualTo(resetPos);
	}

	[Test]
	public async ValueTask reset_all_to_position_only_affects_checkpointed_partitions(CancellationToken ct) {
		// Arrange — only 2 out of 4 partitions have been checkpointed
		var processorId              = CheckpointTestHelpers.ProcessorId("es-reset-all-partial");
		var store                    = CreateStore(processorId, partitionCount: 4);
		var workerId                 = WorkerId();
		var (resetPos, rs, rr)     = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(2, PosData().pos, PosData().stream, PosData().rev)
		), ct);

		// Act
		await store.ResetAsync(workerId, PunyId.New(), RecordPosition.From(resetPos, rs, Revision.From(rr)), ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — only partitions 0 and 2 (the ones that existed), not 1 and 3
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(2);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(resetPos);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(resetPos);
	}

	[Test]
	public async ValueTask reset_all_with_unset_position_is_full_wipe(CancellationToken ct) {
		// Arrange
		var processorId          = CheckpointTestHelpers.ProcessorId("es-reset-unset");
		var store                = CreateStore(processorId, partitionCount: 3);
		var workerId             = WorkerId();
		var (p0, s0, r0)       = PosData();
		var (p1, s1, r1)       = PosData();
		var (p2, s2, r2)       = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions((0, p0, s0, r0), (1, p1, s1, r1), (2, p2, s2, r2)), ct);

		// Act — RecordPosition.Unset = full wipe
		await store.ResetAsync(workerId, PunyId.New(), RecordPosition.Unset, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		await Assert.That(checkpoint).IsEqualTo(ProcessorCheckpoint.Empty);
	}

	[Test]
	public async ValueTask reset_all_then_store_overwrites_reset_positions(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-reset-all-store");
		var store                    = CreateStore(processorId, partitionCount: 3);
		var workerId                 = WorkerId();
		var (resetPos, rs, rr)     = PosData();
		var newPos0                 = resetPos + Faker.Random.Long(1, 5000);
		var (_, ns0, nr0)          = PosData();

		await store.StoreAsync(workerId, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev),
			(2, PosData().pos, PosData().stream, PosData().rev)
		), ct);
		await store.ResetAsync(workerId, PunyId.New(), RecordPosition.From(resetPos, rs, Revision.From(rr)), ct);
		await store.StoreAsync(workerId, PunyId.New(), Positions((0, newPos0, ns0, nr0)), ct);

		// Act
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — partition 0 updated, partitions 1,2 still at reset position
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(newPos0);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(resetPos);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(resetPos);
	}

	#endregion

	#region Properties

	[Test]
	public async ValueTask processor_id_property_matches_constructor_argument(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-prop");
		var store       = CreateStore(processorId);

		// Assert
		await Assert.That(store.ProcessorId).IsEqualTo(processorId);
	}

	[Test]
	public async ValueTask partition_count_below_one_throws(CancellationToken ct) {
		// Act & Assert
		await Assert.That(() => new EventSourcedCheckpointStore(
			"test", 0, 1000, Fixture.Client.Streams
		)).ThrowsExactly<ArgumentOutOfRangeException>();
	}

	#endregion

	#region Distributed Workers

	[Test]
	public async ValueTask workers_checkpointing_different_partitions_at_different_times(CancellationToken ct) {
		// Arrange
		var processorId              = CheckpointTestHelpers.ProcessorId("es-distributed");
		var store1                   = CreateStore(processorId, partitionCount: 4);
		var store2                   = CreateStore(processorId, partitionCount: 4);
		var worker1                  = WorkerId();
		var worker2                  = WorkerId();
		var (pos0, s0, r0)         = PosData();
		var (pos1, s1, r1)         = PosData();
		var (pos2, s2, r2)         = PosData();
		var (pos3, s3, r3)         = PosData();
		var pos0Updated             = pos0 + Faker.Random.Long(1, 5000);
		var pos1Updated             = pos1 + Faker.Random.Long(1, 5000);
		var (_, s0u, r0u)          = PosData();
		var (_, s1u, r1u)          = PosData();

		// Worker 1 checkpoints partitions 0,1
		await store1.StoreAsync(worker1, PunyId.New(), Positions((0, pos0, s0, r0), (1, pos1, s1, r1)), ct);

		// Worker 2 checkpoints partitions 2,3
		await store2.StoreAsync(worker2, PunyId.New(), Positions((2, pos2, s2, r2), (3, pos3, s3, r3)), ct);

		// Worker 1 checkpoints again (new cycle)
		await store1.StoreAsync(worker1, PunyId.New(), Positions((0, pos0Updated, s0u, r0u), (1, pos1Updated, s1u, r1u)), ct);

		// Act
		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(4);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0Updated);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1Updated);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(pos2);
		await Assert.That(byPartition[Partition.From(3)].Position.LogPosition.Value).IsEqualTo(pos3);
	}

	[Test]
	public async ValueTask worker_failure_preserves_other_workers_checkpoints(CancellationToken ct) {
		// Arrange
		var processorId                  = CheckpointTestHelpers.ProcessorId("es-failure");
		var store1                       = CreateStore(processorId, partitionCount: 4);
		var store2                       = CreateStore(processorId, partitionCount: 4);
		var worker1                      = WorkerId();
		var worker2                      = WorkerId();
		var (pos2, s2, r2)             = PosData();
		var (pos3, s3, r3)             = PosData();
		var (pos0Final, s0f, r0f)      = PosData();
		var (pos1Final, s1f, r1f)      = PosData();

		// Both workers checkpoint
		await store1.StoreAsync(worker1, PunyId.New(), Positions(
			(0, PosData().pos, PosData().stream, PosData().rev),
			(1, PosData().pos, PosData().stream, PosData().rev)
		), ct);
		await store2.StoreAsync(worker2, PunyId.New(), Positions((2, pos2, s2, r2), (3, pos3, s3, r3)), ct);

		// Worker 1 checkpoints again (worker 2 crashed)
		await store1.StoreAsync(worker1, PunyId.New(), Positions((0, pos0Final, s0f, r0f), (1, pos1Final, s1f, r1f)), ct);

		// Act
		var checkpoint = await store1.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition.Count).IsEqualTo(4);
		await Assert.That(byPartition[Partition.From(0)].Position.LogPosition.Value).IsEqualTo(pos0Final);
		await Assert.That(byPartition[Partition.From(1)].Position.LogPosition.Value).IsEqualTo(pos1Final);
		await Assert.That(byPartition[Partition.From(2)].Position.LogPosition.Value).IsEqualTo(pos2);
		await Assert.That(byPartition[Partition.From(3)].Position.LogPosition.Value).IsEqualTo(pos3);
	}

	#endregion

	#region Stream Position Round-Trip

	[Test]
	public async ValueTask store_and_load_preserves_stream_and_revision(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-stream-rt");
		var store       = CreateStore(processorId, partitionCount: 1);
		var workerId    = WorkerId();
		var logPos      = Pos();
		var stream      = $"orders-{Faker.Random.AlphaNumeric(8)}";
		var revision    = Faker.Random.Int(0, 1000);

		var position  = RecordPosition.From(logPos, stream, Revision.From(revision), Partition.From(0));
		var positions = new[] { new PartitionPosition(Partition.From(0), position) };

		// Act
		await store.StoreAsync(workerId, PunyId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		var loaded = checkpoint.Positions[0].Position;
		await Assert.That(loaded.LogPosition.Value).IsEqualTo(logPos);
		await Assert.That(loaded.Stream.ToString()).IsEqualTo(stream);
		await Assert.That(loaded.Revision).IsEqualTo(Revision.From(revision));
	}

	[Test]
	public async ValueTask store_and_load_preserves_stream_across_multiple_partitions(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-stream-multi");
		var store       = CreateStore(processorId, partitionCount: 3);
		var workerId    = WorkerId();
		var stream0     = $"orders-{Faker.Random.AlphaNumeric(8)}";
		var stream1     = $"invoices-{Faker.Random.AlphaNumeric(8)}";
		var pos0        = Pos();
		var pos1        = Pos();
		var rev0        = Faker.Random.Int(0, 500);
		var rev1        = Faker.Random.Int(0, 500);

		var positions = new[] {
			new PartitionPosition(Partition.From(0), RecordPosition.From(pos0, stream0, Revision.From(rev0), Partition.From(0))),
			new PartitionPosition(Partition.From(1), RecordPosition.From(pos1, stream1, Revision.From(rev1), Partition.From(1)))
		};

		// Act
		await store.StoreAsync(workerId, PunyId.New(), positions, ct);
		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert
		var byPartition = checkpoint.Positions.ToDictionary(x => x.Partition);
		await Assert.That(byPartition[Partition.From(0)].Position.Stream.ToString()).IsEqualTo(stream0);
		await Assert.That(byPartition[Partition.From(0)].Position.Revision).IsEqualTo(Revision.From(rev0));
		await Assert.That(byPartition[Partition.From(1)].Position.Stream.ToString()).IsEqualTo(stream1);
		await Assert.That(byPartition[Partition.From(1)].Position.Revision).IsEqualTo(Revision.From(rev1));
	}

	[Test]
	public async ValueTask subsequent_store_updates_stream_position(CancellationToken ct) {
		// Arrange
		var processorId = CheckpointTestHelpers.ProcessorId("es-stream-update");
		var store       = CreateStore(processorId, partitionCount: 1);
		var workerId    = WorkerId();
		var stream      = $"orders-{Faker.Random.AlphaNumeric(8)}";
		var pos1        = Pos();
		var pos2        = pos1 + Faker.Random.Long(1, 5000);
		var rev1        = Faker.Random.Int(0, 500);
		var rev2        = rev1 + Faker.Random.Int(1, 100);

		await store.StoreAsync(workerId, PunyId.New(), [
			new(Partition.From(0), RecordPosition.From(pos1, stream, Revision.From(rev1), Partition.From(0)))
		], ct);

		// Act — store updated position for same partition
		await store.StoreAsync(workerId, PunyId.New(), [
			new(Partition.From(0), RecordPosition.From(pos2, stream, Revision.From(rev2), Partition.From(0)))
		], ct);

		var checkpoint = await store.LoadLatestAsync(ct);

		// Assert — latest values
		var loaded = checkpoint.Positions[0].Position;
		await Assert.That(loaded.LogPosition.Value).IsEqualTo(pos2);
		await Assert.That(loaded.Stream.ToString()).IsEqualTo(stream);
		await Assert.That(loaded.Revision).IsEqualTo(Revision.From(rev2));
	}

	#endregion
}
