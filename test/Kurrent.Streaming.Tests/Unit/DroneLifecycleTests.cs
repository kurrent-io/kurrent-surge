using System.Collections.Immutable;
using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Kurrent.Streaming.Tests.Fakes;
using Kurrent.Testkit.TUnit.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
public class DroneLifecycleTests {
	static readonly RecordFaker Records = new();

	static (Drone drone, ControllableSubscription subscription) CreateDrone(
		IProcessor?               processor    = null,
		bool                      debug        = false,
		Action<ProcessorOptions>? configure    = null,
		TimeProvider?             timeProvider = null
	) {
		processor    ??= new TestableRecordProcessor();
		timeProvider ??= TimeProvider.System;

		var settings = new ProcessorOptions {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Debug       = debug
		};

		// Apply per-test overrides before the checkpointer reads DroneId / Checkpoint / InitialPosition.
		configure?.Invoke(settings);

		var channel = Channel.CreateUnbounded<SubscribeMessage>();

		var store = new InMemoryCheckpointStore();

		var checkpointer = new Checkpointer(
			settings.DroneId, settings.Checkpoint,
			store, channel.Writer, timeProvider);

		var subscription = new ControllableSubscription();
		subscription.SetWriter(channel.Writer);

		var drone = new Drone(
			settings, subscription, checkpointer, channel,
			null!, // StreamsClient not needed for lifecycle tests
			NullLoggerFactory.Instance, timeProvider,
			processor);

		return (drone, subscription);
	}

	#region ->> cleanup <<-

	[Test]
	public async ValueTask cleanup_runs_on_normal_completion() {
		// Arrange
		var (drone, subscription) = CreateDrone();

		// Act — empty subscription completes immediately
		subscription.Complete();
		await drone.RunAsync(CancellationToken.None);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask cleanup_runs_on_cancellation(CancellationToken ct) {
		// Arrange
		var cts = new CancellationTokenSource();
		var (drone, subscription) = CreateDrone();

		// Act — pre-cancelled token; subscription reads the token and completes
		await cts.CancelAsync();
		subscription.Complete();
		await drone.RunAsync(cts.Token);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	[Test]
	public async ValueTask cleanup_is_idempotent() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();

		// Act — first call triggers cleanup via RunAsync
		await drone.RunAsync(CancellationToken.None);

		// Assert — second call throws started guard, not double-dispose
		await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	#endregion // cleanup

	#region ->> run_async guards <<-

	[Test]
	public async ValueTask run_async_throws_if_called_twice() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();
		await drone.RunAsync(CancellationToken.None);

		// Act & Assert
		await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
			.Throws<InvalidOperationException>();
	}

	#endregion // run_async guards

	#region ->> status transitions <<-

	[Test]
	public async ValueTask status_is_stopped_on_clean_shutdown() {
		// Arrange
		var (drone, subscription) = CreateDrone();
		subscription.Complete();

		// Act
		await drone.RunAsync(CancellationToken.None);

		// Assert
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	#endregion // status transitions

	#region ->> auto-track contract <<-

	// Records carry the drone's single owned partition (0) and a strictly increasing log position
	// starting at 1. Earliest is -1, so position >= 1 clears the catch-up floor and satisfies the
	// tracker's strictly-monotonic contract; Partition.Any (the faker default) would miss the
	// per-partition tracker and throw on Track, so the partition is pinned to 0 here.
	static Record[] RecordsAt(int count) =>
		Enumerable.Range(1, count)
			.Select(i => Records.Generate() with {
				Position = RecordPosition.From(LogPosition.From(i), Partition.From(0))
			})
			.ToArray();

	[Test]
	[Timeout(15_000)]
	public async ValueTask disabled_auto_track_does_not_track_processed_records(CancellationToken ct) {
		// Arrange — auto-track off; the processor never advances progress itself
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — every record was dispatched, but nothing was tracked, so no checkpoint ever committed
		await Assert.That(processor.Records.Length).IsEqualTo(records.Length);
		await Assert.That(processor.Checkpoints.Count).IsEqualTo(0);
		await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask enabled_auto_track_tracks_processed_records_and_commits_on_shutdown(CancellationToken ct) {
		// Arrange — auto-track on (default); the processor never tracks, so any committed position
		// can only come from the framework's auto-track. This is the contrast to the disabled case.
		var processor       = new TestableRecordProcessor();
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the framework auto-tracked each record; the end-of-stream barrier commits the last position
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(processor.Records.Length).IsEqualTo(records.Length);
		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask disabled_auto_track_allows_and_honors_manual_track(CancellationToken ct) {
		// Arrange — auto-track off; the processor advances progress itself via ctx.Track
		Exception? caught          = null;
		var        records         = RecordsAt(3);
		var        lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestableRecordProcessor {
			OnProcessRecord = ctx => {
				try { ctx.Track(ctx.Record); }
				catch (Exception ex) { caught = ex; }
				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — ctx.Track is permitted with auto-checkpoint off and its positions reach a real checkpoint
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(caught).IsNull();
		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask disabled_auto_track_honors_manual_checkpoint_request(CancellationToken ct) {
		// Arrange — auto-track off; the processor tracks then requests a manual barrier on the last
		// record. StopProcessing (not Complete) ends the run so the barrier is written while the channel
		// is still open — completing the inbox first would race the barrier write and drop it.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestableRecordProcessor {
			OnProcessRecord = ctx => {
				ctx.Track(ctx.Record);

				if (ctx.Record.Position.LogPosition >= lastLogPosition) {
					ctx.Checkpoint("manual-flush");
					ctx.StopProcessing();
				}

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — the manual barrier fired with its reason and carried the tracked position
		var manualCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Manual)
			.ToList();

		await Assert.That(manualCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(manualCheckpoints[0].Reason).IsEqualTo("manual-flush");
		await Assert.That(manualCheckpoints[0].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	#endregion // auto-track contract

	#region ->> interval timer gate <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask interval_checkpoint_fires_when_auto_checkpoint_enabled(CancellationToken ct) {
		// Arrange — auto-checkpoint on, driven by a fake clock. Records auto-track; advancing past the
		// interval must raise an Interval commit barrier without the stream ending.
		var time      = new FakeTimeProvider();
		var interval  = TimeSpan.FromSeconds(30);
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
			opts.Checkpoint.AutoCheckpoint    = true;
			opts.Checkpoint.Interval          = interval;
		}, timeProvider: time);

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act — run without completing the stream; once records are auto-tracked, advance the fake
		// clock past the interval so the timer fires a checkpoint into the still-open channel.
		var run = drone.RunAsync(ct);

		await Assert.That(() => processor.Records.Length)
			.EventuallyPasses(r => r.IsEqualTo(records.Length));

		time.Advance(interval + TimeSpan.FromSeconds(1));

		await Assert.That(() => processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Interval))
			.EventuallyPasses(c => c.IsGreaterThanOrEqualTo(1));

		subscription.Complete();
		await run;

		// Assert — an interval-triggered checkpoint committed the tracked positions
		var intervalCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Interval)
			.ToList();

		await Assert.That(intervalCheckpoints.Count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask interval_checkpoint_does_not_fire_when_auto_checkpoint_disabled(CancellationToken ct) {
		// Arrange — auto-checkpoint off; the interval timer must never start. Auto-track stays on
		// (default), so positions are pending — yet advancing the clock raises no Interval barrier.
		var time      = new FakeTimeProvider();
		var interval  = TimeSpan.FromSeconds(30);
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
			opts.Checkpoint.AutoCheckpoint    = false;
			opts.Checkpoint.Interval          = interval;
		}, timeProvider: time);

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		var run = drone.RunAsync(ct);

		await Assert.That(() => processor.Records.Length)
			.EventuallyPasses(r => r.IsEqualTo(records.Length));

		// Act — advance well past several intervals; the disabled timer stays silent.
		time.Advance(interval * 5);
		await Task.Delay(200, ct);

		// Assert — no interval checkpoint fired while running (the shutdown commit only happens after Complete).
		var intervalWhileRunning = processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Interval);

		subscription.Complete();
		await run;

		await Assert.That(intervalWhileRunning).IsEqualTo(0);
	}

	#endregion // interval timer gate

	#region ->> watermark / OnCheckpoint <<-

	// A server scan watermark (FilterCheckpoint / CaughtUpCheckpoint) carries only a log position —
	// its partition is incidental (here a deliberately non-owned partition 7), so the drone folds it
	// by log position across its owned partitions.
	static FilterCheckpoint WatermarkAt(long logPosition) =>
		new(CheckpointId.New(), DateTimeOffset.UnixEpoch) {
			Positions = [RecordPosition.From(LogPosition.From(logPosition), Partition.From(7))]
		};

	[Test]
	[Timeout(15_000)]
	public async ValueTask auto_track_off_surfaces_watermark_and_commits_nothing(CancellationToken ct) {
		// Arrange — auto-track off; a scan watermark must reach OnCheckpoint with ScanPosition set,
		// but the framework advances nothing, so no checkpoint is committed.
		var watermark = LogPosition.From(5);
		var processor = new TestableRecordProcessor();

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		subscription.EmitCheckpoint(WatermarkAt(watermark.Value));
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — OnCheckpoint saw the watermark (ScanPosition set to the scan's log position), and
		// nothing was committed because the handler did not track it.
		var filterCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Filter)
			.ToList();

		await Assert.That(filterCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(filterCheckpoints[0].ScanPosition).IsNotNull();
		await Assert.That(filterCheckpoints[0].ScanPosition!.Value.LogPosition).IsEqualTo(watermark);
		await Assert.That(drone.Progress.Checkpoint.TotalCheckpoints).IsEqualTo(0L);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask auto_track_off_handler_can_advance_past_watermark(CancellationToken ct) {
		// Arrange — auto-track off; the handler claims the watermark via ctx.Track(ctx.ScanPosition!).
		// Because OnCheckpoint runs before the commit snapshot, that track lands in this barrier.
		var watermark = LogPosition.From(5);

		var processor = new TestableRecordProcessor {
			OnCheckpointHook = ctx => {
				if (ctx.ScanPosition is { } scanPosition)
					ctx.Track(scanPosition);

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		subscription.EmitCheckpoint(WatermarkAt(watermark.Value));
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the handler's track committed the scanned span at the owned partition (0), even
		// though the watermark itself carried a non-owned partition.
		var ownedPartition = Partition.From(0);

		await Assert.That(drone.Progress.Checkpoint.TotalCheckpoints).IsGreaterThanOrEqualTo(1L);
		await Assert.That(drone.Progress.Partitions[ownedPartition].LastFlushedPosition.LogPosition).IsEqualTo(watermark);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask auto_track_on_folds_watermark_without_handler(CancellationToken ct) {
		// Arrange — auto-track on (default); the framework folds the watermark across owned partitions
		// and commits it without any handler involvement.
		var watermark = LogPosition.From(5);
		var processor = new TestableRecordProcessor();

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		subscription.EmitCheckpoint(WatermarkAt(watermark.Value));
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the watermark was auto-folded and committed at the owned partition.
		var ownedPartition = Partition.From(0);

		await Assert.That(drone.Progress.Checkpoint.TotalCheckpoints).IsGreaterThanOrEqualTo(1L);
		await Assert.That(drone.Progress.Partitions[ownedPartition].LastFlushedPosition.LogPosition).IsEqualTo(watermark);
	}

	#endregion // watermark / OnCheckpoint

	#region ->> batched parity <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask disabled_auto_track_batch_does_not_track(CancellationToken ct) {
		// Arrange — auto-track off; the batch processor never tracks, so nothing is committed even
		// though every record in the batch is dispatched.
		var records   = RecordsAt(3);
		var processor = new TestRecordBatchProcessor(maxBatchSize: 100, batchWindow: TimeSpan.FromSeconds(30));

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the batch was delivered, but nothing tracked, so no checkpoint ever committed
		await Assert.That(processor.Records.Count).IsEqualTo(records.Length);
		await Assert.That(drone.Progress.Checkpoint.TotalCheckpoints).IsEqualTo(0L);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_manual_track_whole_batch_commits_on_shutdown(CancellationToken ct) {
		// Arrange — auto-track off; the processor claims the whole batch via ctx.Track().
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestRecordBatchProcessor(maxBatchSize: 100, batchWindow: TimeSpan.FromSeconds(30)) {
			OnProcessBatch = ctx => { ctx.Track(); return ValueTask.CompletedTask; }
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the whole batch was tracked; the end-of-stream barrier commits the last position
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_manual_track_up_to_prefix_commits_only_prefix(CancellationToken ct) {
		// Arrange — auto-track off; the processor claims only a prefix (records 1..2 of 1..4) via
		// ctx.Track(upTo). Records past upTo are never tracked.
		var records      = RecordsAt(4);
		var prefixUpTo   = records[1].Position;     // log position 2
		var prefixLogPos = prefixUpTo.LogPosition;

		var processor = new TestRecordBatchProcessor(maxBatchSize: 100, batchWindow: TimeSpan.FromSeconds(30)) {
			OnProcessBatch = ctx => { ctx.Track(prefixUpTo); return ValueTask.CompletedTask; }
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — only the prefix was committed; the records past upTo were never tracked
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(prefixLogPos.Value);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_manual_checkpoint_fires_manual_barrier(CancellationToken ct) {
		// Arrange — auto-track on (default); the processor requests a manual barrier after the batch.
		// MaxBatchSize equals the record count so the batch flushes by size while the channel is still
		// open — StopProcessing (not Complete) ends the run so the barrier is written, not dropped.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestRecordBatchProcessor(maxBatchSize: records.Length, batchWindow: TimeSpan.FromSeconds(30)) {
			OnProcessBatch = ctx => {
				ctx.Checkpoint("manual-flush");
				ctx.StopProcessing();
				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — the manual barrier fired with its reason and carried the auto-tracked positions
		var manualCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Manual)
			.ToList();

		await Assert.That(manualCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(manualCheckpoints[0].Reason).IsEqualTo("manual-flush");
		await Assert.That(manualCheckpoints[0].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	#endregion // batched parity

	#region ->> telemetry <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask records_processed_count_when_auto_track_off(CancellationToken ct) {
		// Arrange — auto-track off; throughput counting is decoupled from tracking, so every
		// dispatched record still counts even though nothing is tracked.
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — counted every record despite auto-track being off
		await Assert.That(drone.Progress.Throughput.TotalRecords).IsEqualTo((long)records.Length);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_records_processed_count_when_auto_track_off(CancellationToken ct) {
		// Arrange — auto-track off, batched dispatch; counting is decoupled per record (the batch trap).
		var processor = new TestRecordBatchProcessor(maxBatchSize: 100, batchWindow: TimeSpan.FromSeconds(30));
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — counted every record in the batch despite auto-track being off
		await Assert.That(drone.Progress.Throughput.TotalRecords).IsEqualTo((long)records.Length);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask source_lag_computes_from_probed_head() {
		// Arrange — a fake clock drives the probe; a stubbed head read returns a head 800 bytes and
		// 5 seconds ahead of the committed / last-processed position.
		var time      = new FakeTimeProvider();
		var headTime  = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var head      = Records.Generate() with {
			Position  = RecordPosition.From(LogPosition.From(1000), Partition.From(0)),
			Timestamp = headTime
		};
		var committed = RecordPosition.From(LogPosition.From(200), Partition.From(0));
		var progress  = DroneProgress.Empty with {
			Throughput = ThroughputMetrics.Empty with { LastProcessedAt = headTime.AddSeconds(-5) },
			Partitions = ImmutableDictionary<Partition, PartitionProgress>.Empty
				.Add(Partition.From(0), new PartitionProgress(committed, committed))
		};

		long?   lagBytes   = null;
		double? lagSeconds = null;

		await using var probe = new SourceLagProbe(
			readHead: _ => ValueTask.FromResult(head),
			snapshot: () => progress,
			onSample: (bytes, seconds) => { lagBytes = bytes; lagSeconds = seconds; },
			timeProvider: time);

		// Act — advance past the fixed 10s refresh so the probe samples once.
		probe.Start();
		time.Advance(TimeSpan.FromSeconds(10));

		await Assert.That(() => lagBytes.HasValue).EventuallyPasses(captured => captured.IsTrue());

		// Assert — byte gap is head − committed; second gap is head − last-processed event-time.
		await Assert.That(lagBytes!.Value).IsEqualTo(800L);
		await Assert.That(lagSeconds!.Value).IsEqualTo(5d).Within(0.001);
	}

	#endregion // telemetry

	#region ->> uncommitted progress <<-

	[Test]
	public async ValueTask uncommitted_reflects_tracked_minus_committed() {
		// Arrange — one partition tracked at 500, flushed at 200; 7 records since the last checkpoint.
		var partition = Partition.From(0);
		var progress  = DroneProgress.Empty with {
			Checkpoint = CheckpointProgress.Empty with { RecordsSinceLastCheckpoint = 7 },
			Partitions = new Dictionary<Partition, PartitionProgress> {
				[partition] = new PartitionProgress(
					RecordPosition.From(LogPosition.From(500), partition),
					RecordPosition.From(LogPosition.From(200), partition))
			}
		};

		// Act & Assert — records from RecordsSinceLastCheckpoint; bytes from the per-partition gap.
		await Assert.That(progress.UncommittedRecords).IsEqualTo(7L);
		await Assert.That(progress.UncommittedBytes).IsEqualTo(300L);
		await Assert.That(progress[partition].UncommittedBytes).IsEqualTo(300L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_sums_across_partitions() {
		// Arrange — two partitions with gaps 300 (500−200) and 50 (150−100).
		var p0 = Partition.From(0);
		var p1 = Partition.From(1);

		var progress = DroneProgress.Empty with {
			Partitions = new Dictionary<Partition, PartitionProgress> {
				[p0] = new PartitionProgress(RecordPosition.From(LogPosition.From(500), p0), RecordPosition.From(LogPosition.From(200), p0)),
				[p1] = new PartitionProgress(RecordPosition.From(LogPosition.From(150), p1), RecordPosition.From(LogPosition.From(100), p1))
			}
		};

		// Act & Assert
		await Assert.That(progress.UncommittedBytes).IsEqualTo(350L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_before_first_flush() {
		// Arrange — tracked but never flushed: no committed baseline, so the byte gap is zero.
		var partition = Partition.From(0);

		var progress = DroneProgress.Empty with {
			Partitions = new Dictionary<Partition, PartitionProgress> {
				[partition] = new PartitionProgress(RecordPosition.From(LogPosition.From(500), partition), RecordPosition.Unset)
			}
		};

		// Act & Assert
		await Assert.That(progress.UncommittedBytes).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_when_flushed_overtakes_tracked() {
		// Arrange — a committed scan watermark can advance flushed past the last tracked record (the
		// no-match region folds into the checkpointer, never into per-record tracking). The gap floors
		// at zero rather than going negative.
		var partition = Partition.From(0);

		var progress = DroneProgress.Empty with {
			Partitions = new Dictionary<Partition, PartitionProgress> {
				[partition] = new PartitionProgress(RecordPosition.From(LogPosition.From(200), partition), RecordPosition.From(LogPosition.From(500), partition))
			}
		};

		// Act & Assert
		await Assert.That(progress.UncommittedBytes).IsEqualTo(0L);
	}

	#endregion // uncommitted progress

}
