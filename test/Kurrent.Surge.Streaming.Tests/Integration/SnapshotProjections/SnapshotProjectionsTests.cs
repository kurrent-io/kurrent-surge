using Kurrent.SnapshotProjections;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Processors.Gateway.Messages;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;
using Kurrent.Surge.Streaming.Tests.Fakes;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using Kurrent.Testkit.Datasets.ArcadeRacer.Model;

namespace Kurrent.Surge.Streaming.Tests;

[Category("SnapshotProjections")]
[Category("Integration")]
[Timeout(30_000)]
public class SnapshotProjectionsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // RaceCar is the snapshot type because it is mapped by the testkit's message catalog and has a
    // parameterless constructor — a snapshot type must be appendable and reconstructable from nothing.
    static readonly RecordFaker Records = new();

    SnapshotProjectionsStore CreateStore() => new(Fixture.Client.Streams);

    static StreamName NewSnapshotStream() => $"snapshot-projection-{TestId.New()}";

    RaceCar NewRaceCar() => new() {
        Id     = Fixture.Faker.Random.Guid().ToString(),
        Name   = Fixture.Faker.Vehicle.Model(),
        Number = Fixture.Faker.Random.Int(1, 99),
        Pilot  = Fixture.Faker.Name.FullName()
    };

    #region ->> Store <<-

    [Test]
    public async ValueTask loads_empty_snapshot_when_stream_was_never_written(CancellationToken ct) {
        // Arrange
        var store         = CreateStore();
        var stream        = NewSnapshotStream();
        var expectedEmpty = new RaceCar();

        // Act
        var (snapshot, position, timestamp) = await store.LoadSnapshot<RaceCar>(stream, ct);

        // Assert
        await Assert.That(snapshot.Id).IsEqualTo(expectedEmpty.Id);
        await Assert.That(snapshot.Number).IsEqualTo(expectedEmpty.Number);
        await Assert.That(position).IsEqualTo(RecordPosition.Unset);
        await Assert.That(timestamp).IsEqualTo(DateTimeOffset.MinValue);
    }

    [Test]
    public async ValueTask round_trips_snapshot_and_timestamp(CancellationToken ct) {
        // Arrange
        var store             = CreateStore();
        var stream            = NewSnapshotStream();
        var expectedSnapshot  = NewRaceCar();
        var expectedTimestamp = Fixture.Faker.Date.RecentOffset().ToUniversalTime();
        var expectedRevision  = Revision.From(0);

        // Act
        await store.SaveSnapshot(expectedSnapshot, stream, ExpectedStreamState.NoStream,
            expectedTimestamp, ct);

        var (snapshot, position, timestamp) = await store.LoadSnapshot<RaceCar>(stream, ct);

        // Assert
        await Assert.That(snapshot.Id).IsEqualTo(expectedSnapshot.Id);
        await Assert.That(snapshot.Name).IsEqualTo(expectedSnapshot.Name);
        await Assert.That(snapshot.Number).IsEqualTo(expectedSnapshot.Number);
        await Assert.That(snapshot.Pilot).IsEqualTo(expectedSnapshot.Pilot);
        await Assert.That(timestamp).IsEqualTo(expectedTimestamp);
        await Assert.That(position.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask rejects_save_guarded_on_a_stale_revision(CancellationToken ct) {
        // Arrange
        var store            = CreateStore();
        var stream           = NewSnapshotStream();
        var timestamp        = Fixture.Faker.Date.RecentOffset().ToUniversalTime();
        var staleRevision    = Revision.From(Fixture.Faker.Random.Long(5, 100));
        var expectedRevision = Revision.From(0);

        await store.SaveSnapshot(NewRaceCar(), stream, ExpectedStreamState.NoStream,
            timestamp, ct);

        // Act
        var exception = await Assert.ThrowsAsync<StreamRevisionConflictException>(
            async () => await store.SaveSnapshot(NewRaceCar(), stream, staleRevision,
                timestamp, ct));

        // Assert
        await Assert.That(exception!.ExpectedRevision).IsEqualTo(staleRevision);
        await Assert.That(exception.ActualRevision).IsEqualTo(expectedRevision);
    }

    #endregion // Store

    #region ->> Module <<-

    [Test]
    public async ValueTask creates_snapshot_from_the_first_message(CancellationToken ct) {
        // Arrange
        var store  = CreateStore();
        var stream = NewSnapshotStream();
        var module = new LapProjection(store, stream);

        var message = new LapRecorded { CarId = Fixture.Faker.Random.Guid().ToString(), Lap = Fixture.Faker.Random.Int(1, 60) };
        var record  = NewDecodedRecord(message);

        var expectedRevision  = Revision.From(0);
        var expectedTimestamp = new DateTimeOffset(record.Timestamp.ToUniversalTime());

        // Act
        await Dispatch(module, record, ct);

        // Assert
        var (snapshot, position, timestamp) = await store.LoadSnapshot<RaceCar>(stream, ct);

        await Assert.That(snapshot.Id).IsEqualTo(message.CarId);
        await Assert.That(snapshot.Number).IsEqualTo(message.Lap);
        await Assert.That(position.Revision).IsEqualTo(expectedRevision);
        await Assert.That(timestamp).IsEqualTo(expectedTimestamp);
    }

    [Test]
    public async ValueTask advances_the_guarded_revision_across_consecutive_updates(CancellationToken ct) {
        // Arrange
        var store  = CreateStore();
        var stream = NewSnapshotStream();
        var module = new LapProjection(store, stream);

        var carId       = Fixture.Faker.Random.Guid().ToString();
        var firstLap    = Fixture.Faker.Random.Int(1, 30);
        var expectedLap = firstLap + 1;

        var first  = NewDecodedRecord(new LapRecorded { CarId = carId, Lap = firstLap });
        var second = NewDecodedRecord(new LapRecorded { CarId = carId, Lap = expectedLap });

        var expectedRevision = Revision.From(1);

        // Act
        await Dispatch(module, first, ct);
        await Dispatch(module, second, ct);

        // Assert
        var (snapshot, position, _) = await store.LoadSnapshot<RaceCar>(stream, ct);

        await Assert.That(snapshot.Number).IsEqualTo(expectedLap);
        await Assert.That(position.Revision).IsEqualTo(expectedRevision);
    }

    [Test]
    public async ValueTask skips_the_append_when_the_handler_reports_no_change(CancellationToken ct) {
        // Arrange
        var store  = CreateStore();
        var stream = NewSnapshotStream();
        var module = new NoChangeProjection(store, stream);
        var record = NewDecodedRecord(new LapRecorded { CarId = Fixture.Faker.Random.Guid().ToString(), Lap = 1 });

        // Act
        await Dispatch(module, record, ct);

        // Assert — nothing was written, so the snapshot stream still does not exist
        var (_, position, _) = await store.LoadSnapshot<RaceCar>(stream, ct);

        await Assert.That(position.IsUnset).IsTrue();
    }

    [Test]
    public async ValueTask wraps_handler_failures_in_a_snapshot_projection_exception(CancellationToken ct) {
        // Arrange
        var store  = CreateStore();
        var stream = NewSnapshotStream();
        var module = new FailingProjection(store, stream);
        var record = NewDecodedRecord(new LapRecorded { CarId = Fixture.Faker.Random.Guid().ToString(), Lap = 1 });

        // Act
        var exception = await Assert.ThrowsAsync<SnapshotProjectionException>(
            async () => await Dispatch(module, record, ct));

        // Assert
        await Assert.That(exception!.InnerException).IsTypeOf<InvalidOperationException>();
    }

    [Test]
    public async ValueTask propagates_cancellation_without_wrapping_it(CancellationToken ct) {
        // Arrange
        var store  = CreateStore();
        var stream = NewSnapshotStream();
        var module = new LapProjection(store, stream);
        var record = NewDecodedRecord(new LapRecorded { CarId = Fixture.Faker.Random.Guid().ToString(), Lap = 1 });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await cts.CancelAsync();

        // Act & Assert — a cancelled host must not be reported as a projection failure
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await Dispatch(module, record, cts.Token));
    }

    #endregion // Module

    #region ->> Catch-up <<-

    [Test]
    public async ValueTask completes_the_catch_up_signal_after_replaying_history(CancellationToken ct) {
        // Arrange
        var source = await Fixture.SeedTestStream(5, ct);
        var module = new LapCountProjection(CreateStore(), NewSnapshotStream());
        var drone  = CreateDrone(module, source.Stream, SubscriptionInitialPosition.Earliest);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var run = drone.RunAsync(cts.Token).AsTask();

        var (position, _) = await module.WaitUntilCaughtUp.WaitAsync(CatchUpTimeout, ct);

        // Assert
        await Assert.That(module.IsCaughtUp).IsTrue();
        await Assert.That(position.IsUnset).IsFalse();

        await StopDrone(cts, run);
    }

    [Test]
    public async ValueTask completes_the_catch_up_signal_when_there_is_no_history(CancellationToken ct) {
        // Arrange — the stream has records, but the projection starts at the live edge, so there is
        // nothing to replay. This is the case that leaves a catch-up wait pending forever if the
        // signal is only raised on the transition out of a replay.
        var source = await Fixture.SeedTestStream(5, ct);
        var module = new LapCountProjection(CreateStore(), NewSnapshotStream());
        var drone  = CreateDrone(module, source.Stream, SubscriptionInitialPosition.Latest);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Act
        var run = drone.RunAsync(cts.Token).AsTask();

        await module.WaitUntilCaughtUp.WaitAsync(CatchUpTimeout, ct);

        // Assert
        await Assert.That(module.IsCaughtUp).IsTrue();

        await StopDrone(cts, run);
    }

    [Test]
    public async ValueTask cancels_the_catch_up_signal_when_the_processor_shuts_down_first(CancellationToken ct) {
        // Arrange
        var module = new LapCountProjection(CreateStore(), NewSnapshotStream());

        // Act — the shutdown barrier is the last thing a processor emits
        await ((IProcessor)module).OnCheckpoint(NewCheckpointContext(CheckpointTrigger.Shutdown));

        // Assert — a waiter fails loudly rather than hanging on a signal that can no longer arrive
        await Assert.That(module.IsCaughtUp).IsFalse();
        await Assert.ThrowsAsync<TaskCanceledException>(async () => await module.WaitUntilCaughtUp);
    }

    #endregion // Catch-up

    #region ->> Helpers <<-

    static readonly TimeSpan CatchUpTimeout = TimeSpan.FromSeconds(15);

    KurrentProcessor CreateDrone(IRecordProcessor processor, StreamName sourceStream, SubscriptionInitialPosition initialPosition) =>
        KurrentProcessor.Create(processor, opts => {
            opts.Connection(
                Fixture.ConnectionString,
                client => {
                    client.LoggerFactory = Fixture.LoggerFactory;
                    // Typed handlers dispatch on the decoded message type, so this self-built client
                    // must opt into the Testkit catalog to resolve incoming records to LapCompleted.
                    client.Messages.UseTestkitCatalog();
                });

            opts.ProcessorId                  = $"snapshot-projection-{TestId.New()}";
            opts.Subscription.Filter          = RecordFilter.ByStream(sourceStream);
            opts.Subscription.InitialPosition = initialPosition;
            opts.Checkpoint.Interval          = TimeSpan.FromMilliseconds(100);
        });

    static async Task StopDrone(CancellationTokenSource cts, Task run) {
        await cts.CancelAsync();

        try {
            await run;
        }
        catch (OperationCanceledException) {
            // Stopping the drone is how the run ends here.
        }
    }

    CheckpointContext NewCheckpointContext(CheckpointTrigger trigger) =>
        new(_ => { }) {
            Positions    = [],
            CheckpointId = Fixture.Faker.Random.Guid().ToString(),
            Trigger      = trigger,
            Timestamp    = Fixture.Faker.Date.RecentOffset()
        };

    // The module dispatches on the record's decoded value, so the message never crosses the wire in
    // these tests — only the snapshot does. That keeps the folded message type out of the catalog.
    static Record NewDecodedRecord<TMessage>(TMessage message) where TMessage : class {
        var record = Records.Generate();

        record.Value     = message;
        record.ValueType = typeof(TMessage);

        return record;
    }

    static ValueTask Dispatch(IRecordProcessor processor, Record record, CancellationToken ct) =>
        processor.ProcessRecord(
            new ProcessRecordContext(_ => { }) {
                Record            = record,
                Progress          = DroneProgress.Empty,
                CancellationToken = ct
            });

    /// <summary>
    /// A message folded into the snapshot. Never appended, so it needs no message-catalog entry.
    /// </summary>
    public class LapRecorded {
        public string CarId { get; set; } = "";
        public int    Lap   { get; set; }
    }

    sealed class LapProjection : SnapshotProjectionsModule<RaceCar> {
        public LapProjection(ISnapshotProjectionsStore store, StreamName snapshotStream) : base(store, snapshotStream) =>
            UpdateWhen<LapRecorded>((snapshot, message) => {
                snapshot.Id     = message.CarId;
                snapshot.Number = message.Lap;

                return snapshot;
            });
    }

    sealed class NoChangeProjection : SnapshotProjectionsModule<RaceCar> {
        public NoChangeProjection(ISnapshotProjectionsStore store, StreamName snapshotStream) : base(store, snapshotStream) =>
            UpdateWhen<LapRecorded>((_, _) => EmptySnapshot);
    }

    /// <summary>
    /// Folds a catalog-mapped event, so it can run behind a real processor reading real records.
    /// </summary>
    sealed class LapCountProjection : SnapshotProjectionsModule<RaceCar> {
        public LapCountProjection(ISnapshotProjectionsStore store, StreamName snapshotStream) : base(store, snapshotStream) =>
            UpdateWhen<LapCompleted>((snapshot, message) => {
                snapshot.Id     = message.CarId;
                snapshot.Number = message.Lap;

                return snapshot;
            });
    }

    sealed class FailingProjection : SnapshotProjectionsModule<RaceCar> {
        public FailingProjection(ISnapshotProjectionsStore store, StreamName snapshotStream) : base(store, snapshotStream) =>
            UpdateWhen<LapRecorded>((_, _) => throw new InvalidOperationException("Handler failed"));
    }

    #endregion // Helpers
}
