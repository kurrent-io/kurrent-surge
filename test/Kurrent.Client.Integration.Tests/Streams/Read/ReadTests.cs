using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Shouldly;
using Kurrent.Testing.TUnit;
using Serilog;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
public class ReadTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_log(CancellationToken ct) {
        // var home = HomeAutomationDataSet.Default.Home();
        // var stream = StreamName.From($"{nameof(SmartHomeActivity)}-{home.Id}");

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stoppingToken = cancellator.Token;

        var simulation = await Fixture.SeedSmartHomeActivity(10, stoppingToken);

        var options = new ReadOptions {
            Start             = LogPosition.Earliest,
            Filter            = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
            // Filter            = RecordFilter.ByStream(simulation.Stream),
            SkipDecoding      = true,
            Heartbeat         = HeartbeatOptions.Threshold(100),
            CancellationToken = stoppingToken
        };

        await using var messages = await Fixture.Client.Streams
            .ReadRecords(options)
            .ShouldNotThrowOrFailAsync();

        var records = new List<Record>();

        await foreach (var msg in messages.WithCancellation(stoppingToken)) {
            msg.Switch(
                record => {
                    records.Add(record);

                    Log.Information(
                        "Record {RecordId} {SchemaName} @ {Position}",
                        record.Id, record.Schema.SchemaName, record.Position
                    );

                    if (record.Position.LogPosition >= simulation.Position)
                        // ReSharper disable once AccessToDisposedClosure
                        cancellator.Cancel();
                },
                heartbeat => Log.Information("Heartbeat {HeartbeatType} @ {Position}", heartbeat.Type, heartbeat.Position)
            );
        }

        records.Count.ShouldBe(simulation.Records.Count);
    }

    [Test]
    public async ValueTask reads_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var now = Fixture.Time.GetUtcNow();

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        records.Count.ShouldBe(simulation.Records.Count);

        for (var i = 0; i < records.Count; i++) {
            var record   = records[i];
            var iotEvent = simulation.Records[i];

            record.Stream.ShouldBe(simulation.Stream);
            record.StreamRevision.ShouldBeEquivalentTo(StreamRevision.From(i));
            record.Position.LogPosition.ShouldBeLessThanOrEqualTo(simulation.Position);

            record.Schema.SchemaName.Value.ShouldNotBeEmpty();
            record.Schema.SchemaFormat.ShouldBe(iotEvent.DataFormat);
            record.Schema.SchemaVersionId.ShouldBe(SchemaVersionId.None);

            record.Value.ShouldBeEquivalentTo(iotEvent.Value);
            record.ValueType.ShouldBe(iotEvent.Value.GetType());

            record.Timestamp.ShouldNotBeInRange(now.DateTime, Fixture.Time.GetUtcNow().DateTime);

            record.Metadata.Stringify().ShouldBeEquivalentTo(iotEvent.Metadata.Stringify());
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(100)]
    public async ValueTask gracefully_stops_reading_and_flushes_queue_when_read_token_is_cancelled(int records, CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(records, ct);

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, cancellator.Token)
            .ShouldNotThrowOrFailAsync();

        var cancelOn = simulation.Records.Count / 2 + simulation.Records.Count % 2;

        await foreach (var _ in messages)
            if (--cancelOn == 0 && !cancellator.IsCancellationRequested)
                await cancellator.CancelAsync();

        messages.QueuedMessages.ShouldBe(0, "Should have flushed all messages on cancellation");
        cancellator.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(100)]
    public async ValueTask gracefully_stops_reading_without_flushing_queue_when_enumerable_token_is_cancelled(int records, CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(records, ct);

        await using RecordStream messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var cancelOn = simulation.Records.Count / 2 + simulation.Records.Count % 2;

        using var cancellator = new CancellationTokenSource();

        await foreach (var _ in messages.WithCancellation(cancellator.Token)) {
            if (--cancelOn == 0)
                cancellator.Cancel();
            else
                await Task.Delay(25); // simulate some processing time
        }

        messages.QueuedMessages.ShouldBeGreaterThanOrEqualTo(
            cancelOn, "Should not queue more than one message before cancellation"
        );

        cancellator.Token.IsCancellationRequested.ShouldBeTrue();
        cancellator.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    public async ValueTask reads_first_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var expectedRecord = simulation.Records.First();

        await Fixture.Client.Streams
            .ReadFirstStreamRecord(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(record => {
                record.Value.ShouldBeEquivalentTo(expectedRecord.Value);
                record.Id.ShouldBe(expectedRecord.RecordId);
            });
    }

    [Test]
    public async ValueTask reads_last_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var expectedRecord = simulation.Records.Last();

        await Fixture.Client.Streams
            .ReadLastStreamRecord(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(record => {
                record.Value.ShouldBeEquivalentTo(expectedRecord.Value);
                record.Id.ShouldBe(expectedRecord.RecordId);
            });
    }

    public class NonExistingPositions : TestCaseGenerator<LogPosition> {
        protected override IEnumerable<LogPosition> Data() => [
            LogPosition.Latest, // edge case as it represents the end of the log
            LogPosition.Latest - 1,
            LogPosition.Latest - 2
        ];
    }
    //
    // [Test, NonExistingPositions]
    // public async ValueTask fails_with_log_position_not_found_when_log_position_does_not_exist(LogPosition position, CancellationToken ct) {
    //     await Fixture.Client.Streams
    //         .Read(position, ct)
    //         .ShouldFailAsync(err => err
    //             .Value.ShouldBeOfType<LogPositionNotFoundException>()
    //             .Message.ShouldContain($"{position}"));
    // }

    [Test]
    public async ValueTask fails_with_log_position_not_found_when_log_position_is_broken(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        RecordStream messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .UnwrapAsync();

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        Log.Information("Stream Position After Append: {StreamPosition}", simulation.Position);

        for (var i = 0; i < records.Count; i++) {
            var record = records[i];
            Log.Information(
                "{Sequence:00} Record {RecordId} Position: {Position}", i + 1, record.Id,
                record.Position);
        }

        // does not exist between records
        var brokenPosition = LogPosition.From(simulation.Position - 1);

        await Fixture.Client.Streams
            .ReadRecords(brokenPosition, ct)
            .ShouldFailAsync(err => err
                .Value.ShouldBeOfType<LogPositionNotFoundException>()
                .Message.ShouldContain($"{brokenPosition}"));
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .ShouldFailAsync(err => err.Value.ShouldBeOfType<StreamDeletedException>());
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct)
            .ShouldFailAsync(err => err.Value.ShouldBeOfType<StreamTombstonedException>());
    }

    [Test]
    public async ValueTask fails_with_stream_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .ReadStreamRecords(activity.Stream, ct)
            .ShouldFailAsync(err => err.Value.ShouldBeOfType<StreamNotFoundException>());
    }
}
