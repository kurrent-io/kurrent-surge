using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Processors.Tests.Fakes;
using Kurrent.Testkit.TUnit.Assertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Processors.Tests;

[Category("Processors")]
public class ManualCheckpointingTests {
    static readonly RecordFaker Records = new();

    #region ->> ProcessRecordContext API <<-

    [Test]
    public async ValueTask checkpoint_throws_when_called_twice_in_same_process_record() {
        // Arrange
        var ctx = new ProcessRecordContext(_ => { }) {
            Record   = Records.Generate(),
            Progress = DroneProgress.Empty
        };

        // Act
        ctx.Checkpoint("first");

        // Assert
        await Assert.That(() => ctx.Checkpoint("second"))
            .Throws<InvalidOperationException>()
            .WithMessageMatching("a checkpoint has already been requested for this record");
    }

    [Test]
    public async ValueTask checkpoint_throws_operation_canceled_when_token_is_cancelled() {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ctx = new ProcessRecordContext(_ => { }) {
            Record            = Records.Generate(),
            Progress          = DroneProgress.Empty,
            CancellationToken = cts.Token
        };

        // Act & Assert
        await Assert.That(() => ctx.Checkpoint("x"))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async ValueTask checkpoint_stashes_request_for_drone_to_pick_up() {
        // Arrange
        var ctx = new ProcessRecordContext(_ => { }) {
            Record   = Records.Generate(),
            Progress = DroneProgress.Empty
        };

        // Act
        ctx.Checkpoint("flushed-batch");

        // Assert — drone reads CheckpointRequested + CheckpointReason after auto-track
        // and calls _source.InjectCheckpoint(Manual, reason).
        await Assert.That(ctx.CheckpointRequested).IsTrue();
        await Assert.That(ctx.CheckpointReason).IsEqualTo("flushed-batch");
    }

    [Test]
    public async ValueTask track_record_forwards_record_position_to_request_track_delegate() {
        // Arrange
        var capturedPositions = new List<RecordPosition>();
        var record            = Records.Generate();

        var expectedPositions = new List<RecordPosition> { record.Position };

        var ctx = new ProcessRecordContext(capturedPositions.Add) {
            Record   = record,
            Progress = DroneProgress.Empty
        };

        // Act
        ctx.Track(record);

        // Assert
        await Assert.That(capturedPositions).IsDeepEquivalentTo(expectedPositions);
    }

    [Test]
    public async ValueTask track_positions_forwards_each_position_to_request_track_delegate() {
        // Arrange
        var capturedPositions = new List<RecordPosition>();
        var positions         = Records.Generate(3).Select(r => r.Position).ToArray();

        var expectedPositions = positions.ToList();

        var ctx = new ProcessRecordContext(capturedPositions.Add) {
            Record   = Records.Generate(),
            Progress = DroneProgress.Empty
        };

        // Act
        ctx.Track(positions);

        // Assert
        await Assert.That(capturedPositions).IsDeepEquivalentTo(expectedPositions);
    }

    #endregion

    #region ->> Drone-driven scenarios <<-

    [Test]
    [Timeout(15_000)]
    public async ValueTask track_is_allowed_when_auto_checkpoint_is_true(CancellationToken ct) {
        // Arrange — explicit Track in AutoCheckpoint=true mode is valid; the tracker
        // is idempotent if both user and framework track the same position.
        var source        = new ControllableSource();
        Exception? caught = null;

        var processor = new RecordingProcessor {
            OnProcessRecord = ctx => {
                try {
                    ctx.Track(ctx.Record);
                }
                catch (Exception ex) {
                    caught = ex;
                }
                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(source, processor, autoCheckpoint: true);
        var run   = drone.RunAsync(CancellationToken.None);

        var record = Records.Generate();

        // Act
        source.EmitConnected();
        source.EmitRecord(record);
        source.Complete();

        await run;

        // Assert
        await Assert.That(caught).IsNull();
        await Assert.That(source.TrackedPositions).Contains(record.Position);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask manual_track_works_when_auto_checkpoint_is_false(CancellationToken ct) {
        // Arrange
        var source = new ControllableSource();
        var record = Records.Generate();

        var expectedPositions = new List<RecordPosition> { record.Position };
        
        var processor = new RecordingProcessor {
            OnProcessRecord = ctx => {
                ctx.Track(ctx.Record);
                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(source, processor, autoCheckpoint: false);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitRecord(record);
        source.Complete();

        await run;

        // Assert
        await Assert.That(source.TrackedPositions).IsDeepEquivalentTo(expectedPositions);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask auto_checkpoint_false_skips_framework_auto_track(CancellationToken ct) {
        // Arrange
        var source    = new ControllableSource();
        var record    = Records.Generate();

        // Processor does NOT call Track — verifies framework doesn't auto-track either.
        var processor = new RecordingProcessor();

        var drone = CreateDrone(source, processor, autoCheckpoint: false);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitRecord(record);
        source.Complete();

        await run;

        // Assert — no positions tracked at all
        await Assert.That(source.TrackedPositions).IsEmpty();
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask manual_checkpoint_raises_on_checkpoint_with_trigger_manual_and_reason(CancellationToken ct) {
        // Arrange
        var source     = new ControllableSource();
        var checkpoint = (CheckpointTrigger?)null;
        string? reason = null;

        // Signal from OnCheckpoint so the test waits until the Manual marker has been
        // dispatched — closing the channel earlier races with HandleRecord's InjectCheckpoint.
        var manualCheckpointFired = new TaskCompletionSource();

        var processor = new RecordingProcessor {
            OnProcessRecord = ctx => {
                ctx.Checkpoint("custom-reason");
                return ValueTask.CompletedTask;
            },
            OnCheckpointHook = ctx => {
                if (ctx.Trigger == CheckpointTrigger.Manual) {
                    checkpoint = ctx.Trigger;
                    reason     = ctx.Reason;
                    manualCheckpointFired.TrySetResult();
                }
                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(source, processor, autoCheckpoint: false);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitRecord(Records.Generate());
        await manualCheckpointFired.Task;
        source.Complete();

        await run;

        // Assert
        await Assert.That(checkpoint).IsEqualTo(CheckpointTrigger.Manual);
        await Assert.That(reason).IsEqualTo("custom-reason");
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask on_checkpoint_returning_persists_checkpoint(CancellationToken ct) {
        // Arrange
        var source         = new ControllableSource();
        var ckpt           = NewCheckpoint(CheckpointTrigger.Interval);

        var processor = new RecordingProcessor {
            OnCheckpointHook = _ => ValueTask.CompletedTask
        };

        var drone = CreateDrone(source, processor);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitCheckpoint(ckpt);
        source.Complete();

        await run;

        // Assert
        await Assert.That(source.CompletedCheckpoints).Contains(ckpt.CheckpointId);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask on_checkpoint_throwing_skips_persistence_when_failure_policy_is_continue(CancellationToken ct) {
        // Arrange
        var source = new ControllableSource();
        var ckpt   = NewCheckpoint(CheckpointTrigger.Interval);

        var processor = new RecordingProcessor {
            OnCheckpointHook = _ => throw new InvalidOperationException("user gate")
        };

        var drone = CreateDrone(source, processor, failurePolicy: CheckpointFailurePolicy.Continue);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitCheckpoint(ckpt);
        source.Complete();

        await run;

        // Assert — drone reaches Stopped (no tear-down), checkpoint NOT persisted
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
        await Assert.That(source.CompletedCheckpoints).DoesNotContain(ckpt.CheckpointId);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask on_checkpoint_throwing_tears_down_drone_when_failure_policy_is_fail(CancellationToken ct) {
        // Arrange
        var source = new ControllableSource();
        var ckpt   = NewCheckpoint(CheckpointTrigger.Interval);

        var processor = new RecordingProcessor {
            OnCheckpointHook = _ => throw new InvalidOperationException("user gate")
        };

        var drone = CreateDrone(source, processor, failurePolicy: CheckpointFailurePolicy.Fail);

        // Act
        source.EmitConnected();
        source.EmitCheckpoint(ckpt);
        source.Complete();

        await Assert.That(async () => await drone.RunAsync(CancellationToken.None))
            .Throws<AggregateException>();

        // Assert — drone went through error path
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Error);
        await Assert.That(source.CompletedCheckpoints).DoesNotContain(ckpt.CheckpointId);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask on_checkpoint_throw_under_continue_policy_does_not_tear_down(CancellationToken ct) {
        // Arrange — OnCheckpoint throws on Interval, returns on Manual; FailurePolicy=Continue
        // → drone keeps running, only Manual is persisted
        var source = new ControllableSource();

        var manualCkpt   = NewCheckpoint(CheckpointTrigger.Manual);
        var intervalCkpt = NewCheckpoint(CheckpointTrigger.Interval);

        var processor = new RecordingProcessor {
            OnCheckpointHook = ctx => ctx.Trigger == CheckpointTrigger.Interval
                ? throw new InvalidOperationException("only manual triggers are committed")
                : ValueTask.CompletedTask
        };

        var drone = CreateDrone(source, processor, failurePolicy: CheckpointFailurePolicy.Continue);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.EmitCheckpoint(intervalCkpt);
        source.EmitCheckpoint(manualCkpt);
        source.Complete();

        await run;

        // Assert
        await Assert.That(drone.Status).IsEqualTo(DroneStatus.Stopped);
        await Assert.That(source.CompletedCheckpoints).DoesNotContain(intervalCkpt.CheckpointId);
        await Assert.That(source.CompletedCheckpoints).Contains(manualCkpt.CheckpointId);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask shutdown_raises_on_checkpoint_with_trigger_shutdown(CancellationToken ct) {
        // Arrange
        var source            = new ControllableSource();
        var triggers          = new ConcurrentBag<CheckpointTrigger>();

        var processor = new RecordingProcessor {
            OnCheckpointHook = ctx => {
                triggers.Add(ctx.Trigger);
                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(source, processor);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act — emit Connected then close. ControllableSource emits a Shutdown marker
        // after Complete() to mirror the production source's behavior.
        source.EmitConnected();
        source.CompleteWithShutdownMarker();

        await run;

        // Assert
        await Assert.That(triggers).Contains(CheckpointTrigger.Shutdown);
    }

    [Test]
    [Timeout(15_000)]
    public async ValueTask shutdown_with_throwing_on_checkpoint_still_calls_on_complete(CancellationToken ct) {
        // Arrange
        var source       = new ControllableSource();
        var completeFired = false;

        var processor = new RecordingProcessor {
            OnCheckpointHook = ctx => ctx.Trigger == CheckpointTrigger.Shutdown
                ? throw new InvalidOperationException("shutdown gate failed")
                : ValueTask.CompletedTask,
            OnCompleteHook = _ => {
                completeFired = true;
                return ValueTask.CompletedTask;
            }
        };

        var drone = CreateDrone(source, processor, failurePolicy: CheckpointFailurePolicy.Continue);
        var run   = drone.RunAsync(CancellationToken.None);

        // Act
        source.EmitConnected();
        source.CompleteWithShutdownMarker();

        await run;

        // Assert — shutdown marker not persisted, but OnComplete fires
        await Assert.That(completeFired).IsTrue();
    }

    #endregion

    #region ->> PartitionedPositionTracker validation <<-

    [Test]
    public async ValueTask tracker_rejects_position_that_is_not_ahead_of_last_flushed() {
        // Arrange
        var partition = Partition.From(0);
        var first     = Records.Generate().Position with { Partition = partition };
        var second    = Records.Generate().Position with { Partition = partition };

        using var tracker = new PartitionedPositionTracker([second]);

        // Act — try to track a position equal to or below the initial flushed position.
        // PositionTracker requires positions strictly greater than LastFlushedPosition.
        await Assert.That(() => tracker.Track(first)).Throws<InvalidOperationException>();
    }

    #endregion

    #region ->> Helpers <<-

    static Drone CreateDrone(
        ControllableSource source,
        IRecordProcessor processor,
        bool autoCheckpoint = true,
        CheckpointFailurePolicy failurePolicy = CheckpointFailurePolicy.Continue,
        bool debug = false
    ) {
        var settings = new ProcessorOptions {
            ProcessorId = "test-processor",
            DroneId     = "test-drone",
            Debug       = debug,
            Checkpoint  = new CheckpointOptions {
                AutoCheckpoint = autoCheckpoint,
                FailurePolicy  = failurePolicy
            }
        };

        return new Drone(
            settings,
            source,
            processor,
            NullLoggerFactory.Instance,
            TimeProvider.System);
    }

    static Checkpoint NewCheckpoint(CheckpointTrigger trigger, string? reason = null) =>
        new Checkpoint(
            CheckpointId.From(Guid.NewGuid().ToString()),
            trigger,
            DateTimeOffset.UtcNow,
            []
        ) { Reason = reason };

    #endregion
}

/// <summary>
/// In-memory subscription source that lets tests inject specific messages and observe
/// what Track / InjectCheckpoint / CompleteCheckpoint calls the drone makes.
/// </summary>
sealed class ControllableSource : ISubscriptionSource {
    readonly Channel<SubscribeMessage> _channel = Channel.CreateUnbounded<SubscribeMessage>();
    readonly List<RecordPosition>      _tracked = [];

    readonly List<(CheckpointTrigger trigger, string? reason)> _injected  = new();
    readonly ConcurrentDictionary<string, byte>                _completed = new();

    int _shutdownInjected;

    public IReadOnlyList<RecordPosition> TrackedPositions => _tracked;

    public IReadOnlyList<(CheckpointTrigger trigger, string? reason)> InjectedCheckpoints => _injected;

    public ICollection<string> CompletedCheckpoints => _completed.Keys;

    public async IAsyncEnumerable<SubscribeMessage> Subscribe([EnumeratorCancellation] CancellationToken ct) {
        // Drain non-cancellable so the Shutdown marker can be processed even when ct fires.
        while (await _channel.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false)) {
            while (_channel.Reader.TryRead(out var msg))
                yield return msg;
        }
    }

    public void Track(Record record)           => _tracked.Add(record.Position);
    public void Track(RecordPosition position) => _tracked.Add(position);

    public void Track(IEnumerable<Record> records) {
        foreach (var record in records)
            _tracked.Add(record.Position);
    }

    public ValueTask InjectCheckpoint(CheckpointTrigger trigger, string? reason = null) {
        if (trigger == CheckpointTrigger.Shutdown
            && Interlocked.Exchange(ref _shutdownInjected, 1) == 1)
            return ValueTask.CompletedTask;

        _injected.Add((trigger, reason));

        var marker = new Checkpoint(
            CheckpointId.From(Guid.NewGuid().ToString()),
            trigger,
            DateTimeOffset.UtcNow,
            []
        ) { Reason = reason };

        _channel.Writer.TryWrite(marker);

        if (trigger == CheckpointTrigger.Shutdown)
            _channel.Writer.TryComplete();

        return ValueTask.CompletedTask;
    }

    public ValueTask CompleteCheckpoint(Checkpoint checkpoint, CancellationToken ct = default) {
        _completed[checkpoint.CheckpointId] = 1;
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public void EmitConnected() =>
        _channel.Writer.TryWrite(new Connected(SessionId.From(Guid.NewGuid())));

    public void EmitRecord(Record record) =>
        _channel.Writer.TryWrite(record);

    public void EmitCheckpoint(Checkpoint ckpt) =>
        _channel.Writer.TryWrite(ckpt);

    public void Complete() =>
        _channel.Writer.TryComplete();

    public void CompleteWithShutdownMarker() {
        // Mirrors what the production source does on cancellation: inject Shutdown,
        // which writes the marker and completes the writer atomically.
        _ = InjectCheckpoint(CheckpointTrigger.Shutdown, "test shutdown");
    }
}

sealed class RecordingProcessor : IRecordProcessor {
    public Func<ProcessRecordContext, ValueTask>? OnProcessRecord  { get; set; }
    public Func<CheckpointContext,    ValueTask>? OnCheckpointHook { get; set; }
    public Func<CompleteContext,      ValueTask>? OnCompleteHook   { get; set; }

    public ValueTask ProcessRecord(ProcessRecordContext context) =>
        OnProcessRecord?.Invoke(context) ?? ValueTask.CompletedTask;

    public ValueTask OnCheckpoint(CheckpointContext context) =>
        OnCheckpointHook?.Invoke(context) ?? ValueTask.CompletedTask;

    public ValueTask OnComplete(CompleteContext context) =>
        OnCompleteHook?.Invoke(context) ?? ValueTask.CompletedTask;
}
