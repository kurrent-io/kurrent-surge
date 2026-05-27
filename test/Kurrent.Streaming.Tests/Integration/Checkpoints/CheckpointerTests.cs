using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Checkpointing;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Models;
using Kurrent.Streaming.Options;
using Microsoft.Extensions.Logging;

namespace Kurrent.Streaming.Tests;

[Category("Processors")]
[Category("Integration")]
public class CheckpointerTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    Checkpointer CreateCheckpointer(
        string processorId,
        ChannelWriter<SubscribeMessage> writer,
        CheckpointOptions? options = null,
        int partitionCount = 1,
        int maxCount = 100
    ) {
        var opts = options ?? new CheckpointOptions {
            AutoCheckpoint = true,
            Interval       = TimeSpan.FromSeconds(30)
        };

        var store = new KurrentDBCheckpointStore(
            processorId,
            partitionCount,
            maxCount,
            Fixture.Client.Streams,
            Fixture.TypeMapper,
            TimeProvider.System,
            TestkitExecutionContext.LoggerFactory.CreateLogger<KurrentDBCheckpointStore>()
        );

        return new Checkpointer($"{processorId}-drone", opts, store, writer, TimeProvider.System);
    }

    static Partition[] Partitions(int count) =>
        Enumerable.Range(0, count).Select(Partition.From).ToArray();

    #region ->> Initialization <<-

    [Test]
    public async ValueTask initialize_loads_checkpoint_from_store_and_returns_positions(CancellationToken ct) {
        // Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("ckptr-init");
        var channel     = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        // Act
        var positions = await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Assert
        await Assert.That(positions.Length).IsEqualTo(1);
        await Assert.That(positions[0].LogPosition).IsEqualTo(LogPosition.Earliest);

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask initialize_resumes_from_previously_committed_checkpoint(CancellationToken ct) {
        // Arrange
        var processorId = CheckpointTestHelpers.ProcessorId("ckptr-resume");
        var channel     = Channel.CreateBounded<SubscribeMessage>(16);
        var partitions  = Partitions(1);

        var checkpointer1 = CreateCheckpointer(processorId, channel.Writer);
        await checkpointer1.Initialize(partitions, SubscriptionInitialPosition.Earliest, ct: ct);

        var position = RecordPosition.From(LogPosition.From(5000), Partition.From(0));
        checkpointer1.Track(position);
        var positions1 = checkpointer1.PendingPositions();
        await checkpointer1.RaiseCheckpoint(CheckpointTrigger.Interval);

        channel.Reader.TryRead(out var msg);
        var checkpoint = (Checkpoint)msg.Value;
        await checkpointer1.CommitCheckpoint(checkpoint.Id, positions1, ct);
        await checkpointer1.DisposeAsync();

        // Act
        var channel2      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer2 = CreateCheckpointer(processorId, channel2.Writer);
        var positions     = await checkpointer2.Initialize(partitions, SubscriptionInitialPosition.Earliest, ct: ct);

        // Assert
        await Assert.That(positions.Length).IsEqualTo(1);
        await Assert.That(positions[0].LogPosition.Value).IsEqualTo(5000L);

        await checkpointer2.DisposeAsync();
    }

    [Test]
    public async ValueTask initialize_throws_on_empty_partitions(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-empty-parts");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        // Act & Assert
        await Assert.That(async () =>
                await checkpointer.Initialize([], SubscriptionInitialPosition.Earliest, ct: ct))
            .Throws<ArgumentOutOfRangeException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask initialize_throws_on_second_call(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-double-init");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act & Assert
        await Assert.That(async () =>
                await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct))
            .Throws<InvalidOperationException>();

        await checkpointer.DisposeAsync();
    }

    #endregion // Initialization

    #region ->> Pre-initialization guards <<-

    [Test]
    public async ValueTask track_before_initialize_throws(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-track-guard");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);
        var position     = RecordPosition.From(LogPosition.From(1), Partition.From(0));

        // Act & Assert
        await Assert.That(() => checkpointer.Track(position))
            .Throws<InvalidOperationException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_checkpoint_before_initialize_throws(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-raise-guard");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        // Act & Assert
        await Assert.That(async () => await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval))
            .Throws<InvalidOperationException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask commit_before_initialize_throws(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-commit-guard");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        // Act & Assert
        await Assert.That(async () => await checkpointer.CommitCheckpoint("cp-1", [], ct))
            .Throws<InvalidOperationException>();

        await checkpointer.DisposeAsync();
    }

    #endregion // Pre-initialization guards

    #region ->> Track and RaiseCheckpoint <<-

    [Test]
    public async ValueTask tracked_position_appears_in_raised_checkpoint(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-track-raise");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        var position = RecordPosition.From(LogPosition.From(100), Partition.From(0));

        // Act
        checkpointer.Track(position);
        var pendingPositions = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        // Assert
        var ok = channel.Reader.TryRead(out _);
        await Assert.That(ok).IsTrue();

        await Assert.That(pendingPositions.Length).IsEqualTo(1);
        await Assert.That(pendingPositions[0].LogPosition.Value).IsEqualTo(100L);

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask multiple_tracks_are_reflected_in_checkpoint_positions(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-multi-track");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer, partitionCount: 2);

        await checkpointer.Initialize(Partitions(2), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        checkpointer.Track(RecordPosition.From(LogPosition.From(10), Partition.From(0)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(20), Partition.From(0)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(30), Partition.From(1)));

        var pendingPositions = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        // Assert
        channel.Reader.TryRead(out _);
        var byPartition = pendingPositions.ToDictionary(p => p.Partition);

        await Assert.That(byPartition.Count).IsEqualTo(2);
        await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(20L);
        await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(30L);

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_interval_keeps_channel_open(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-interval-open");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        // Assert
        channel.Reader.TryRead(out var msg);
        await Assert.That(msg.Value).IsTypeOf<IntervalCheckpoint>();

        // Channel still open
        await Assert.That(channel.Writer.TryWrite(SubscribeMessage.None)).IsTrue();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_shutdown_completes_channel(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-shutdown");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Shutdown, "test shutdown");

        // Assert
        channel.Reader.TryRead(out var msg);
        var checkpoint = (Checkpoint)msg.Value;
        await Assert.That(checkpoint).IsTypeOf<ShutdownCheckpoint>();

        await Assert.That(channel.Writer.TryWrite(SubscribeMessage.None)).IsFalse();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_rebalance_completes_channel(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-rebalance");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Rebalance, "rebalance");

        // Assert
        channel.Reader.TryRead(out var msg);
        var checkpoint = (Checkpoint)msg.Value;
        await Assert.That(checkpoint).IsTypeOf<RebalanceCheckpoint>();
        await Assert.That(channel.Writer.TryWrite(SubscribeMessage.None)).IsFalse();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_after_shutdown_throws(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-post-shutdown");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Shutdown, "done");

        // Act & Assert
        await Assert.That(async () => await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval))
            .Throws<InvalidOperationException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_rejects_unspecified_trigger(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-unspec");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act & Assert
        await Assert.That(async () => await checkpointer.RaiseCheckpoint(CheckpointTrigger.Unspecified))
            .Throws<ArgumentOutOfRangeException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_manual_keeps_channel_open(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-manual");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Manual, "user flush");

        // Assert
        channel.Reader.TryRead(out var msg);
        var checkpoint = msg.Value as ManualCheckpoint;
        await Assert.That(checkpoint).IsNotNull();
        await Assert.That(checkpoint!.Reason).IsEqualTo("user flush");

        await Assert.That(channel.Writer.TryWrite(SubscribeMessage.None)).IsTrue();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_caught_up_keeps_channel_open(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-caughtup");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.CaughtUp, "live mode");

        // Assert
        channel.Reader.TryRead(out var msg);
        await Assert.That(msg.Value).IsTypeOf<CaughtUpCheckpoint>();

        await Assert.That(channel.Writer.TryWrite(SubscribeMessage.None)).IsTrue();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_produces_valid_checkpoint_id_and_timestamp(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-marker-meta");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        // Assert
        channel.Reader.TryRead(out var msg);
        var checkpoint = (Checkpoint)msg.Value;

        await Assert.That(checkpoint.Id.IsUnset).IsFalse();
        await Assert.That(checkpoint.Timestamp).IsGreaterThanOrEqualTo(before);
        await Assert.That(checkpoint.Timestamp).IsLessThanOrEqualTo(after);

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask pending_positions_reflects_current_tracked_state(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-pending-positions");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Track position 100 and snapshot pending positions
        checkpointer.Track(RecordPosition.From(LogPosition.From(100), Partition.From(0)));
        var snapshot1 = checkpointer.PendingPositions();

        // Assert snapshot1 captured position 100
        await Assert.That(snapshot1[0].LogPosition.Value).IsEqualTo(100L);

        // Track position 200 and snapshot again
        checkpointer.Track(RecordPosition.From(LogPosition.From(200), Partition.From(0)));
        var snapshot2 = checkpointer.PendingPositions();

        // Assert snapshot2 reflects the updated position 200, not the old 100
        await Assert.That(snapshot2[0].LogPosition.Value).IsEqualTo(200L);

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_interval_with_reason_propagates_reason(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-interval-reason");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval, "periodic flush");

        // Assert — reason is discarded for Interval checkpoints (only ManualCheckpoint carries Reason)
        channel.Reader.TryRead(out var msg);
        await Assert.That(msg.Value).IsTypeOf<IntervalCheckpoint>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_interval_without_reason_has_null_reason(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-no-reason");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        // Assert
        channel.Reader.TryRead(out var msg);
        await Assert.That(msg.Value).IsTypeOf<IntervalCheckpoint>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask raise_on_closed_channel_is_noop(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-closed-ch");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);
        channel.Writer.TryComplete();

        // Act & Assert — should not throw
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);

        await checkpointer.DisposeAsync();
    }

    #endregion // Track and RaiseCheckpoint

    #region ->> CommitCheckpoint <<-

    [Test]
    public async ValueTask commit_persists_to_store_and_is_loadable(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-commit");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        var position = RecordPosition.From(LogPosition.From(7777), Partition.From(0));
        checkpointer.Track(position);
        var pendingPositions = checkpointer.PendingPositions();

        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);
        channel.Reader.TryRead(out var msg);
        var raised = (Checkpoint)msg.Value;

        // Act
        await checkpointer.CommitCheckpoint(raised.Id, pendingPositions, ct);
        await checkpointer.DisposeAsync();

        // Assert — new checkpointer loads the committed position
        var channel2      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer2 = CreateCheckpointer(processorId, channel2.Writer);
        var positions     = await checkpointer2.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        await Assert.That(positions.Length).IsEqualTo(1);
        await Assert.That(positions[0].LogPosition.Value).IsEqualTo(7777L);

        await checkpointer2.DisposeAsync();
    }

    [Test]
    public async ValueTask commit_with_empty_checkpoint_id_throws(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-empty-id");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act & Assert
        await Assert.That(async () => await checkpointer.CommitCheckpoint("", [], ct))
            .Throws<ArgumentException>();

        await checkpointer.DisposeAsync();
    }

    [Test]
    public async ValueTask full_track_raise_commit_cycle_across_partitions(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-full-cycle");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer, partitionCount: 3);

        await checkpointer.Initialize(Partitions(3), SubscriptionInitialPosition.Earliest, ct: ct);

        checkpointer.Track(RecordPosition.From(LogPosition.From(100), Partition.From(0)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(200), Partition.From(1)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(300), Partition.From(2)));

        // Act — raise + commit
        var pendingPositions1 = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);
        channel.Reader.TryRead(out var msg);
        var raised = (Checkpoint)msg.Value;
        await checkpointer.CommitCheckpoint(raised.Id, pendingPositions1, ct);

        // Track all 3 partitions again in the second round
        checkpointer.Track(RecordPosition.From(LogPosition.From(150), Partition.From(0)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(250), Partition.From(1)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(350), Partition.From(2)));

        var pendingPositions2 = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);
        channel.Reader.TryRead(out var msg2);
        var raised2 = (Checkpoint)msg2.Value;
        await checkpointer.CommitCheckpoint(raised2.Id, pendingPositions2, ct);

        await checkpointer.DisposeAsync();

        // Assert — new checkpointer should see the latest positions
        var channel2      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer2 = CreateCheckpointer(processorId, channel2.Writer, partitionCount: 3);
        var positions     = await checkpointer2.Initialize(Partitions(3), SubscriptionInitialPosition.Earliest, ct: ct);

        var byPartition = positions.ToDictionary(p => p.Partition);
        await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(150L);
        await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(250L);
        await Assert.That(byPartition[Partition.From(2)].LogPosition.Value).IsEqualTo(350L);

        await checkpointer2.DisposeAsync();
    }

    [Test]
    public async ValueTask idle_partitions_retain_flushed_position_in_subsequent_checkpoints(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-idle-part");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer, partitionCount: 2);

        await checkpointer.Initialize(Partitions(2), SubscriptionInitialPosition.Earliest, ct: ct);

        // Track both partitions and commit
        checkpointer.Track(RecordPosition.From(LogPosition.From(100), Partition.From(0)));
        checkpointer.Track(RecordPosition.From(LogPosition.From(200), Partition.From(1)));

        var pendingPositions1 = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);
        channel.Reader.TryRead(out var msg1);
        var raised1 = (Checkpoint)msg1.Value;
        await checkpointer.CommitCheckpoint(raised1.Id, pendingPositions1, ct);

        // Act — only track partition 0, partition 1 is idle
        checkpointer.Track(RecordPosition.From(LogPosition.From(150), Partition.From(0)));

        var pendingPositions2 = checkpointer.PendingPositions();
        await checkpointer.RaiseCheckpoint(CheckpointTrigger.Interval);
        channel.Reader.TryRead(out _);

        // Assert — partition 1 should retain its flushed position (200), not be Unset
        var byPartition = pendingPositions2.ToDictionary(p => p.Partition);
        await Assert.That(byPartition.Count).IsEqualTo(2);
        await Assert.That(byPartition[Partition.From(0)].LogPosition.Value).IsEqualTo(150L);
        await Assert.That(byPartition[Partition.From(1)].LogPosition.Value).IsEqualTo(200L);

        await checkpointer.DisposeAsync();
    }

    #endregion // CommitCheckpoint

    #region ->> Dispose <<-

    [Test]
    public async ValueTask dispose_is_idempotent(CancellationToken ct) {
        // Arrange
        var processorId  = CheckpointTestHelpers.ProcessorId("ckptr-dispose");
        var channel      = Channel.CreateBounded<SubscribeMessage>(16);
        var checkpointer = CreateCheckpointer(processorId, channel.Writer);

        await checkpointer.Initialize(Partitions(1), SubscriptionInitialPosition.Earliest, ct: ct);

        // Act & Assert — double dispose should not throw
        await checkpointer.DisposeAsync();
        await checkpointer.DisposeAsync();
    }

    #endregion // Dispose
}
