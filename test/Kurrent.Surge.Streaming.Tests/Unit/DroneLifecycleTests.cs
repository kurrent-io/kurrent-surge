using System.Diagnostics;
using System.Threading.Channels;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Configuration;
using Kurrent.Surge.Streaming.Checkpointing;
using Kurrent.Surge.Streaming.Gateway;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Kurrent.Testkit.TUnit.Assertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Processors")]
public class DroneLifecycleTests {
	static readonly RecordFaker Records = new();

	static (Drone drone, ControllableDataSource subscription) CreateDrone(
		IProcessor?               processor     = null,
		bool                      debug         = false,
		Action<ProcessorOptions>? configure     = null,
		TimeProvider?             timeProvider  = null,
		ICheckpointStore?         store         = null,
		ILoggerFactory?           loggerFactory = null,
		IProcessorObserver[]?     observers     = null
	) {
		processor     ??= new TestableRecordProcessor();
		timeProvider  ??= TimeProvider.System;
		store         ??= new InMemoryCheckpointStore();
		loggerFactory ??= NullLoggerFactory.Instance;

		var settings = new ProcessorOptions {
			ProcessorId = "test-processor",
			DroneId     = "test-drone",
			Debug       = debug
		};

		// Apply per-test overrides before the checkpointer reads DroneId / Checkpoint / InitialPosition.
		configure?.Invoke(settings);

		var channel = Channel.CreateUnbounded<DataSourceMessage>();

		var checkpointer = new Checkpointer(
			settings.DroneId, settings.Checkpoint,
			store, channel.Writer, timeProvider);

		var subscription = new ControllableDataSource();

		var drone = new Drone(
			settings, subscription, checkpointer, channel,
			null!, // StreamsClient not needed for lifecycle tests
			loggerFactory, timeProvider,
			processor,
			observers ?? []);

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
		// Arrange — auto-track off; the processor tracks each record and requests a manual barrier on the
		// last one WITHOUT stopping (a stop would supersede the manual barrier — the shutdown barrier
		// becomes the single commit point). MaxBatchSize equals the record count so the batch flushes by
		// size with the channel still open, so the manual barrier is written, not dropped. The inbox is
		// completed to end the run only AFTER the barrier is observed, avoiding the barrier-write race.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestableRecordProcessor {
			OnProcessRecord = ctx => {
				ctx.Track(ctx.Record);

				if (ctx.Record.Position.LogPosition >= lastLogPosition)
					ctx.Checkpoint("manual-flush");

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act — run, wait for the manual barrier (raised while the channel is open), then complete to end.
		var run = drone.RunAsync(ct);

		await Assert.That(() => processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Manual))
			.EventuallyPasses(c => c.IsGreaterThanOrEqualTo(1));

		subscription.Complete();
		await run;

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
		await Assert.That(drone.Progress.LowestCommittedPosition).IsEqualTo(RecordPosition.Unset);
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

		await Assert.That(drone.Progress.LowestCommittedPosition).IsNotEqualTo(RecordPosition.Unset);
		await Assert.That(drone.Progress.LowestCommittedPosition.LogPosition).IsEqualTo(watermark);
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

		await Assert.That(drone.Progress.LowestCommittedPosition).IsNotEqualTo(RecordPosition.Unset);
		await Assert.That(drone.Progress.LowestCommittedPosition.LogPosition).IsEqualTo(watermark);
	}

	#endregion // watermark / OnCheckpoint

	#region ->> batched parity <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask disabled_auto_track_batch_does_not_track(CancellationToken ct) {
		// Arrange — auto-track off; the batch processor never tracks, so nothing is committed even
		// though every record in the batch is dispatched.
		var records   = RecordsAt(3);
		var processor = new TestRecordBatchProcessor();

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
		await Assert.That(drone.Progress.LowestCommittedPosition).IsEqualTo(RecordPosition.Unset);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_manual_track_whole_batch_commits_on_shutdown(CancellationToken ct) {
		// Arrange — auto-track off; the processor claims the whole batch via ctx.Track().
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestRecordBatchProcessor() {
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

		var processor = new TestRecordBatchProcessor() {
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
		// Arrange — auto-track on (default); the processor requests a manual barrier after the batch
		// WITHOUT stopping (a stop would supersede the manual barrier — the shutdown barrier becomes the
		// single commit point). MaxBatchSize equals the record count so the batch flushes by size with
		// the channel still open, so the manual barrier is written, not dropped. The inbox is completed
		// to end the run only AFTER the barrier is observed, avoiding the barrier-write race.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestRecordBatchProcessor() {
			OnProcessBatch = ctx => {
				ctx.Checkpoint("manual-flush");
				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act — run, wait for the manual barrier (raised while the channel is open), then complete to end.
		var run = drone.RunAsync(ct);

		await Assert.That(() => processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Manual))
			.EventuallyPasses(c => c.IsGreaterThanOrEqualTo(1));

		subscription.Complete();
		await run;

		// Assert — the manual barrier fired with its reason and carried the auto-tracked positions
		var manualCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Manual)
			.ToList();

		await Assert.That(manualCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(manualCheckpoints[0].Reason).IsEqualTo("manual-flush");
		await Assert.That(manualCheckpoints[0].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	#endregion // batched parity

	#region ->> failure-path checkpointing <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask process_batch_failure_commits_tracked_frontier(CancellationToken ct) {
		// Arrange — auto-track off; the batch processor tracks a prefix (records 1..2 of 1..3) then throws.
		// A ProcessBatch failure must still raise the shutdown barrier so OnCheckpoint runs and the tracked
		// frontier commits — the user's durable intent survives the failure instead of the batch replaying whole.
		var records      = RecordsAt(3);
		var prefixUpTo   = records[1].Position;     // log position 2
		var prefixLogPos = prefixUpTo.LogPosition;

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = ctx => {
				ctx.Track(prefixUpTo);
				throw new InvalidOperationException("process batch boom");
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

		// Act — the run faults (ProcessBatch threw), but must flush the tracked prefix on the way out.
		await Assert.That(async () => await drone.RunAsync(ct)).Throws<AggregateException>();

		// Assert — a shutdown barrier committed exactly the tracked prefix; records past upTo never committed.
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(prefixLogPos.Value);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask on_checkpoint_failure_does_not_recommit(CancellationToken ct) {
		// Arrange — auto-track on; a stop raises the shutdown barrier whose OnCheckpoint throws. With
		// FailurePolicy.Fail the throw is fatal. The drone must NOT re-raise the final checkpoint on an
		// OnCheckpoint failure — re-running the failing hook would invoke it twice.
		var records = RecordsAt(3);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch   = ctx => { ctx.StopProcessing(); return ValueTask.CompletedTask; },
			OnCheckpointHook = _   => throw new InvalidOperationException("checkpoint boom")
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;   // flush by size; channel stays open for the barrier
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
			opts.Checkpoint.FailurePolicy     = CheckpointFailurePolicy.Fail;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		// No Complete() — the stop drives shutdown; completing the inbox first would race the barrier write.

		// Act — the failing checkpoint hook surfaces as a fatal error.
		await Assert.That(async () => await drone.RunAsync(ct)).Throws<AggregateException>();

		// Assert — OnCheckpoint fired exactly once; the failing hook was not re-run on the way out.
		await Assert.That(processor.Checkpoints.Count).IsEqualTo(1);
	}

	#endregion // failure-path checkpointing

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
		await Assert.That(drone.Progress.TotalRecords).IsEqualTo((long)records.Length);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_records_processed_count_when_auto_track_off(CancellationToken ct) {
		// Arrange — auto-track off, batched dispatch; counting is decoupled per record (the batch trap).
		var processor = new TestRecordBatchProcessor();
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
		await Assert.That(drone.Progress.TotalRecords).IsEqualTo((long)records.Length);
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
			LastProcessedAt          = headTime.AddSeconds(-5),
			LowestCommittedPosition  = committed,
			HighestCommittedPosition = committed
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

	// The uncommitted accounting moved into ProgressTracker with the partition slots — DroneProgress
	// now carries the result rather than deriving it, so these drive the tracker directly.
	static ProgressTracker NewTracker(int partitionCount = 1) =>
		new(TimeProvider.System, partitionCount);

	static RecordPosition At(long position, int partition) =>
		RecordPosition.From(LogPosition.From(position), Partition.From(partition));

	[Test]
	public async ValueTask uncommitted_reflects_tracked_minus_committed() {
		// Arrange — commit at 200, then dispatch 7 records and track out to 500
		var tracker = NewTracker();
		var record  = Records.Generate();

		tracker.TrackCheckpoint([At(200, 0)]);

		for (var i = 0; i < 7; i++)
			tracker.CountThroughput(record);

		tracker.TrackPosition(At(500, 0));

		// Act
		var progress = tracker.Progress;

		// Assert — records since the commit, bytes from the tracked-to-committed gap
		await Assert.That(progress.UncommittedRecords).IsEqualTo(7L);
		await Assert.That(progress.UncommittedBytes).IsEqualTo(300L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_sums_across_partitions() {
		// Arrange — two partitions with gaps 300 (500−200) and 50 (150−100)
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(200, 0), At(100, 1)]);
		tracker.TrackPosition(At(500, 0));
		tracker.TrackPosition(At(150, 1));

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(350L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_before_first_commit() {
		// Arrange — tracked but never committed: no baseline, so nothing is measurably at risk in bytes
		var tracker = NewTracker();

		tracker.TrackPosition(At(500, 0));

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask uncommitted_bytes_is_zero_when_committed_overtakes_tracked() {
		// Arrange — a committed scan watermark can advance past the last tracked record (the no-match
		// region folds into the checkpointer, never into per-record tracking). The gap floors at zero.
		var tracker = NewTracker();

		tracker.TrackPosition(At(200, 0));
		tracker.TrackCheckpoint([At(500, 0)]);

		// Act & Assert
		await Assert.That(tracker.Progress.UncommittedBytes).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask committed_watermarks_span_the_least_and_most_advanced_partitions() {
		// Arrange — two partitions committed at different positions. The spread between the watermarks
		// is the commit skew; the lowest is where a restart resumes from.
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(900, 0), At(300, 1)]);

		// Act
		var progress = tracker.Progress;

		// Assert
		await Assert.That(progress.LowestCommittedPosition).IsEqualTo(At(300, 1));
		await Assert.That(progress.HighestCommittedPosition).IsEqualTo(At(900, 0));
	}

	[Test]
	public async ValueTask committed_watermarks_ignore_partitions_that_never_committed() {
		// Arrange — one of two partitions has committed. An Unset slot must not drag the lowest to zero,
		// which would read as "durable from the start of the log".
		var tracker = NewTracker(partitionCount: 2);

		tracker.TrackCheckpoint([At(400, 0)]);

		// Act
		var progress = tracker.Progress;

		// Assert
		await Assert.That(progress.LowestCommittedPosition).IsEqualTo(At(400, 0));
		await Assert.That(progress.HighestCommittedPosition).IsEqualTo(At(400, 0));
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_checkpoint_after_stop_throws(CancellationToken ct) {
		// Arrange — once a stop is requested the graceful Shutdown barrier is the single commit point,
		// so a manual checkpoint is meaningless and must throw. The batch handler requests stop, then
		// attempts a checkpoint; the attempt is captured at its source rather than surfacing as a drone
		// fatal error. MaxBatchSize equals the record count so the batch flushes by size with the
		// channel still open — StopProcessing then ends the run via the Shutdown barrier.
		InvalidOperationException? caught = null;
		var                       records = RecordsAt(3);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = ctx => {
				ctx.StopProcessing();

				try { ctx.Checkpoint(); }
				catch (InvalidOperationException ex) { caught = ex; }

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — Checkpoint() after StopProcessing() threw InvalidOperationException
		await Assert.That(caught).IsNotNull();
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask record_checkpoint_after_stop_throws(CancellationToken ct) {
		// Arrange — same guard on the per-record path: the handler requests stop, then attempts a
		// checkpoint, which must throw because the Shutdown barrier is already the single commit point.
		InvalidOperationException? caught = null;
		var                       records = RecordsAt(1);

		var processor = new TestableRecordProcessor {
			OnProcessRecord = ctx => {
				ctx.StopProcessing();

				try { ctx.Checkpoint(); }
				catch (InvalidOperationException ex) { caught = ex; }

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — Checkpoint() after StopProcessing() threw InvalidOperationException
		await Assert.That(caught).IsNotNull();
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_stop_supersedes_pending_checkpoint_single_barrier(CancellationToken ct) {
		// Arrange — a manual checkpoint requested BEFORE a stop is superseded by the stop: the graceful
		// shutdown raises one Shutdown barrier (OnCheckpoint + commit) and no separate Manual barrier.
		// AutoCheckpoint off so the 30s interval timer can never inject a barrier — the count is exact.
		var records = RecordsAt(3);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = ctx => {
				ctx.Checkpoint();
				ctx.StopProcessing();
				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Checkpoint.AutoCheckpoint    = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — exactly one OnCheckpoint fired, and it was the Shutdown barrier, not the superseded Manual
		var manualBarriers   = processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Manual);
		var shutdownBarriers = processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Shutdown);

		await Assert.That(processor.Checkpoints.Count).IsEqualTo(1);
		await Assert.That(manualBarriers).IsEqualTo(0);
		await Assert.That(shutdownBarriers).IsEqualTo(1);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask record_stop_supersedes_pending_checkpoint_single_barrier(CancellationToken ct) {
		// Arrange — same supersession on the per-record path: the handler requests a manual checkpoint
		// then a stop on the last record; the Shutdown barrier must be the single OnCheckpoint + commit,
		// with no separate Manual barrier. AutoCheckpoint off pins the barrier count.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);

		var processor = new TestableRecordProcessor {
			OnProcessRecord = ctx => {
				if (ctx.Record.Position.LogPosition >= lastLogPosition) {
					ctx.Checkpoint();
					ctx.StopProcessing();
				}

				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Checkpoint.AutoCheckpoint    = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);

		// Act
		await drone.RunAsync(ct);

		// Assert — exactly one OnCheckpoint fired, and it was the Shutdown barrier, not the superseded Manual
		var manualBarriers   = processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Manual);
		var shutdownBarriers = processor.Checkpoints.Count(c => c.Trigger == CheckpointTrigger.Shutdown);

		await Assert.That(processor.Checkpoints.Count).IsEqualTo(1);
		await Assert.That(manualBarriers).IsEqualTo(0);
		await Assert.That(shutdownBarriers).IsEqualTo(1);
	}

	#endregion // checkpoint-vs-stop

	#region ->> batch frontier bookkeeping & telemetry <<-

	[Test]
	[Timeout(15_000)]
	public async ValueTask process_batch_emits_one_span_around_processbatch(CancellationToken ct) {
		// Arrange — one batch span must wrap the ProcessBatch call (not the post-batch bookkeeping), so the
		// span is the active Activity while user code runs and is the only span emitted for the batch.
		var       processorId = $"proc-{Guid.NewGuid():N}";
		using var probe       = new DroneTelemetryProbe(processorId);

		string? spanDuringProcess = null;
		var     records           = RecordsAt(3);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = _ => {
				spanDuringProcess = Activity.Current?.OperationName;
				return ValueTask.CompletedTask;
			}
		};

		var (drone, subscription) = CreateDrone(processor, observers: [probe.Observer], configure: opts => {
			opts.ProcessorId                  = processorId;
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — exactly one batch span, and it was the active Activity during ProcessBatch
		await Assert.That(probe.Activities("process").Count).IsEqualTo(1);
		await Assert.That(spanDuringProcess).IsEqualTo("process");
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_duration_measures_process_batch_not_bookkeeping(CancellationToken ct) {
		// Arrange — ProcessBatch sleeps a real interval. The batch duration must include that work; the old
		// code never timed ProcessBatch (it timed only the ~0 post-batch bookkeeping), so it reported near
		// zero. A floor well under the sleep but well above bookkeeping noise distinguishes the two.
		var       processorId = $"proc-{Guid.NewGuid():N}";
		using var probe       = new DroneTelemetryProbe(processorId);

		var processDelay = TimeSpan.FromMilliseconds(60);
		var minSeconds   = 0.025;
		var records      = RecordsAt(3);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = async _ => await Task.Delay(processDelay, ct)
		};

		var (drone, subscription) = CreateDrone(processor, observers: [probe.Observer], configure: opts => {
			opts.ProcessorId                  = processorId;
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — one batch-duration sample, and it captured the ProcessBatch work
		var durations = probe.Values("kurrent_processor_batch_duration_seconds");

		await Assert.That(durations.Count).IsEqualTo(1);
		await Assert.That(durations[0]).IsGreaterThanOrEqualTo(minSeconds);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask records_processed_counts_committed_prefix_once_per_batch(CancellationToken ct) {
		// Arrange — auto-track on commits the whole batch, so the committed prefix equals the batch size.
		// records_processed must be emitted exactly once for the batch (not per record), carrying that prefix.
		var       processorId = $"proc-{Guid.NewGuid():N}";
		using var probe       = new DroneTelemetryProbe(processorId);

		var records       = RecordsAt(3);
		var expectedCount = (double)records.Length;
		var processor     = new TestRecordBatchProcessor();

		var (drone, subscription) = CreateDrone(processor, observers: [probe.Observer], configure: opts => {
			opts.ProcessorId                  = processorId;
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — one records_processed add, valued at the committed prefix (the full batch)
		var counts = probe.Values("kurrent_processor_records_processed");

		await Assert.That(counts.Count).IsEqualTo(1);
		await Assert.That(counts[0]).IsEqualTo(expectedCount);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask record_duration_on_batch_path_is_batch_average(CancellationToken ct) {
		// Arrange — on the batch path record_duration is the per-record AVERAGE: the batch duration divided
		// by the committed-prefix count, emitted once per batch from the same duration the batch histogram uses.
		var       processorId = $"proc-{Guid.NewGuid():N}";
		using var probe       = new DroneTelemetryProbe(processorId);

		var records   = RecordsAt(4);
		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = async _ => await Task.Delay(TimeSpan.FromMilliseconds(40), ct)
		};

		var (drone, subscription) = CreateDrone(processor, observers: [probe.Observer], configure: opts => {
			opts.ProcessorId                  = processorId;
			opts.Batching.MaxBatchSize        = records.Length;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — record_duration == batch_duration / committed count
		var batchDurations  = probe.Values("kurrent_processor_batch_duration_seconds");
		var recordDurations = probe.Values("kurrent_processor_record_duration_seconds");

		await Assert.That(batchDurations.Count).IsEqualTo(1);
		await Assert.That(recordDurations.Count).IsEqualTo(1);

		var expectedAverage = batchDurations[0] / records.Length;
		await Assert.That(recordDurations[0]).IsEqualTo(expectedAverage).Within(1e-9);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask user_track_updates_uncommitted_gauges(CancellationToken ct) {
		// Arrange — auto-track off; the processor claims the batch via ctx.Track(). The uncommitted gauges
		// read from _progress, so a user track must advance per-partition progress (LastTrackedPosition),
		// not only the checkpointer. The old batch path mirrored progress only under auto-track, so user
		// tracks left the gauges stale (the transparency gap this closes).
		var records      = RecordsAt(3);
		var partition    = Partition.From(0);
		var lastPosition = records[^1].Position;

		var processor = new TestRecordBatchProcessor {
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

		// Assert — the user's track reached the frontier and committed there. Progress no longer exposes
		// the tracked position, so this observes the track through the commit it produced: had ctx.Track
		// not landed, nothing would be pending at shutdown and the watermark would stay Unset.
		await Assert.That(drone.Progress.LowestCommittedPosition).IsEqualTo(lastPosition);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask track_upto_then_stop_commits_only_prefix_under_autotrack(CancellationToken ct) {
		// Arrange — auto-track on. The processor tracks a prefix (records 1..2 of 1..4) then stops. Auto-track
		// is suppressed on stop, so the commit is exactly the user's prefix — not the whole batch. This is the
		// case that regressed before: auto-track ignored the stop and committed the full batch, losing the cap.
		var records      = RecordsAt(4);
		var prefixUpTo   = records[1].Position;     // log position 2
		var prefixLogPos = prefixUpTo.LogPosition;

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = ctx => {
				ctx.Track(prefixUpTo);
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
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the shutdown barrier committed only the prefix, despite auto-track being on
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(prefixLogPos.Value);
	}

	// NOTE: the caught-up prune moved off the drone and onto the data source. Its coverage now lives in the
	// KdbDataSource integration tests (caught-up records dropped by the source; Latest/Unset floors not pruned),
	// exercising the real prune rather than a fake. The drone loop below sees only already-pruned batches.

	[Test]
	[Timeout(15_000)]
	public async ValueTask batch_track_tracks_max_position_per_partition(CancellationToken ct) {
		// Arrange — two partitions, interleaved. ctx.Track() must track the MAX position per partition, so the
		// committed frontier is the highest log position seen on each partition, not the last record overall.
		var p0 = Partition.From(0);
		var p1 = Partition.From(1);

		Record RecordAt(long logPosition, Partition partition) =>
			Records.Generate() with { Position = RecordPosition.From(LogPosition.From(logPosition), partition) };

		var records = new[] {
			RecordAt(1, p0), RecordAt(2, p1), RecordAt(3, p0), RecordAt(4, p1)
		};

		var expectedP0 = LogPosition.From(3);
		var expectedP1 = LogPosition.From(4);

		var processor = new TestRecordBatchProcessor {
			OnProcessBatch = ctx => { ctx.Track(); return ValueTask.CompletedTask; }
		};

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.MaxParallelism               = 2;
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — the committed frontier carries the max position per partition
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);

		var committed = shutdownCheckpoints[^1].Positions;
		await Assert.That(committed.Single(p => p.Partition == p0).LogPosition).IsEqualTo(expectedP0);
		await Assert.That(committed.Single(p => p.Partition == p1).LogPosition).IsEqualTo(expectedP1);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask auto_track_off_with_no_tracking_warns_on_shutdown(CancellationToken ct) {
		// Arrange — auto-track off and the processor never tracks. Records are dispatched but nothing is ever
		// committed, so on shutdown the drone must warn exactly once that no progress was persisted.
		var loggerFactory = new CapturingLoggerFactory();
		var records       = RecordsAt(3);
		var processor     = new TestRecordBatchProcessor();

		var (drone, subscription) = CreateDrone(processor, loggerFactory: loggerFactory, configure: opts => {
			opts.Checkpoint.AutoTrack         = false;
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — exactly one shutdown warning naming the lost-progress consequence
		var warnings = loggerFactory.Entries
			.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("replays", StringComparison.OrdinalIgnoreCase))
			.ToList();

		await Assert.That(warnings.Count).IsEqualTo(1);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask latest_start_with_no_checkpoint_dispatches_and_commits(CancellationToken ct) {
		// Arrange — SubscriptionInitialPosition.Latest with no prior checkpoint seeds a Latest (long.MaxValue)
		// floor. That sentinel must NOT prune (every real record is <= Latest, which would silently drop the
		// whole live stream) and must NOT be the tracker's flushed baseline (the monotonic tracker would reject
		// every real position). Auto-track on: the records must reach ProcessBatch and the frontier must commit.
		var records         = RecordsAt(3);
		var lastLogPosition = LogPosition.From(records.Length);
		var processor       = new TestRecordBatchProcessor();

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Latest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — nothing was pruned and the auto-tracked frontier committed on shutdown
		var shutdownCheckpoints = processor.Checkpoints
			.Where(c => c.Trigger == CheckpointTrigger.Shutdown)
			.ToList();

		await Assert.That(processor.Records.Count).IsEqualTo(records.Length);
		await Assert.That(shutdownCheckpoints.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(shutdownCheckpoints[^1].Positions.Max(p => p.LogPosition.Value)).IsEqualTo(lastLogPosition.Value);
	}

	#endregion // batch frontier bookkeeping & telemetry

	#region ->> skipped records <<-

	// A decoded record: MessageProcessor dispatches on the CLR type of the deserialized value, so a
	// record only reaches a handler once both Value and ValueType are set.
	static Record DecodedAs<T>(Record record, T value) =>
		record with { Value = value!, ValueType = typeof(T) };

	[Test]
	[Timeout(15_000)]
	public async ValueTask records_without_a_matching_handler_are_reported_as_skipped(CancellationToken ct) {
		// Arrange — a handler for OrderPlaced only; the stream carries OrderShipped, which nothing handles
		var observer = new RecordingObserver();
		var handled  = 0;

		var records = RecordsAt(3)
			.Select(r => DecodedAs(r, new OrderShipped()))
			.ToArray();

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => handled++);

		var (drone, subscription) = CreateDrone(processor, observers: [observer], configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert — every record was reported skipped, none reached a handler, and none was dispatched
		await Assert.That(observer.Skipped.Count).IsEqualTo(records.Length);
		await Assert.That(observer.Skipped.Select(r => r.Position.LogPosition.Value))
			.IsEquivalentTo(records.Select(r => r.Position.LogPosition.Value));
		await Assert.That(handled).IsEqualTo(0);
		await Assert.That(observer.Completed.Count).IsEqualTo(0);
		await Assert.That(observer.Ready.Count).IsEqualTo(0);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask skipped_records_still_advance_the_checkpoint(CancellationToken ct) {
		// Arrange — nothing handles these records. If skipping did not advance the frontier the
		// subscription would replay the same span on every restart, so the shutdown barrier must still
		// commit the last position.
		var records         = RecordsAt(3).Select(r => DecodedAs(r, new OrderShipped())).ToArray();
		var expectedPosition = LogPosition.From(records.Length).Value;
		var committed        = new List<RecordPosition>();

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.OnCheckpoint(checkpointContext => committed.AddRange(checkpointContext.Positions));

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert
		await Assert.That(committed.Count).IsGreaterThanOrEqualTo(1);
		await Assert.That(committed.Max(p => p.LogPosition.Value)).IsEqualTo(expectedPosition);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask a_fallback_handler_stops_records_being_skipped(CancellationToken ct) {
		// Arrange — a fallback takes everything no typed handler claims, so nothing is ever skipped
		var observer  = new RecordingObserver();
		var fellBack  = 0;

		var records = RecordsAt(3).Select(r => DecodedAs(r, new OrderShipped())).ToArray();

		var processor = new MessageProcessor()
			.Process<OrderPlaced>((_, _) => { })
			.Fallback(_ => fellBack++);

		var (drone, subscription) = CreateDrone(processor, observers: [observer], configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert
		await Assert.That(observer.Skipped.Count).IsEqualTo(0);
		await Assert.That(fellBack).IsEqualTo(records.Length);
		await Assert.That(observer.Completed.Count).IsEqualTo(records.Length);
	}

	[Test]
	[Timeout(15_000)]
	public async ValueTask a_processor_that_does_not_override_can_process_receives_every_record(CancellationToken ct) {
		// Arrange — the default CanProcess returns true, so an ordinary processor sees everything and
		// nothing is reported skipped, decoded or not
		var observer  = new RecordingObserver();
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, observers: [observer], configure: opts => {
			opts.Subscription.InitialPosition = SubscriptionInitialPosition.Earliest;
		});

		subscription.EmitConnected();
		foreach (var record in records)
			subscription.EmitRecord(record);
		subscription.Complete();

		// Act
		await drone.RunAsync(ct);

		// Assert
		await Assert.That(observer.Skipped.Count).IsEqualTo(0);
		await Assert.That(processor.Records.Length).IsEqualTo(records.Length);
	}

	#endregion // skipped records

}

/// <summary>Message types used only to give test records a CLR type to dispatch on.</summary>
file sealed record OrderPlaced;

file sealed record OrderShipped;

/// <summary>Collects the record-level events the drone raises, in order.</summary>
file sealed class RecordingObserver : IProcessorObserver {
	public List<Record> Skipped   { get; } = [];
	public List<Record> Ready     { get; } = [];
	public List<Record> Completed { get; } = [];

	public void OnRecordSkipped(in RecordSkipped evt)     => Skipped.Add(evt.Record);
	public void OnRecordReady(in RecordReady evt)         => Ready.Add(evt.Record);
	public void OnRecordCompleted(in RecordCompleted evt) => Completed.Add(evt.Record);
}
