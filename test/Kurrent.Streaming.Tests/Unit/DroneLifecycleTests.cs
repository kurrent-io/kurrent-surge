using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Kurrent.Streaming.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
public class DroneLifecycleTests {
	static readonly RecordFaker Records = new();

	static (Drone drone, ControllableSubscription subscription) CreateDrone(
		IRecordProcessor?         processor = null,
		bool                      debug     = false,
		Action<ProcessorOptions>? configure = null
	) {
		processor ??= new TestableRecordProcessor();

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
			store, channel.Writer, TimeProvider.System);

		var subscription = new ControllableSubscription();
		subscription.SetWriter(channel.Writer);

		var drone = new Drone(
			settings, subscription, checkpointer, channel,
			null!, // StreamsClient not needed for lifecycle tests
			NullLoggerFactory.Instance, TimeProvider.System,
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

	#region ->> auto-checkpoint contract <<-

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
	public async ValueTask disabled_auto_checkpoint_does_not_track_processed_records(CancellationToken ct) {
		// Arrange — auto-checkpoint off; the processor never advances progress itself
		var processor = new TestableRecordProcessor();
		var records   = RecordsAt(3);

		var (drone, subscription) = CreateDrone(processor, configure: opts => {
			opts.Checkpoint.AutoCheckpoint    = false;
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
	public async ValueTask enabled_auto_checkpoint_tracks_processed_records_and_commits_on_shutdown(CancellationToken ct) {
		// Arrange — auto-checkpoint on (default); the processor never tracks, so any committed position
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
	public async ValueTask disabled_auto_checkpoint_allows_and_honors_manual_track(CancellationToken ct) {
		// Arrange — auto-checkpoint off; the processor advances progress itself via ctx.Track
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
			opts.Checkpoint.AutoCheckpoint    = false;
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
	public async ValueTask disabled_auto_checkpoint_honors_manual_checkpoint_request(CancellationToken ct) {
		// Arrange — auto-checkpoint off; the processor tracks then requests a manual barrier on the last
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
			opts.Checkpoint.AutoCheckpoint    = false;
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

	#endregion // auto-checkpoint contract

}
