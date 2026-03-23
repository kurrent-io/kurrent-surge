using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testkit;
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

        var options = ReadLog.FromStart()
            .WithFilterExpression($"stream = '{simulation.Stream}'")
            .SkipDecoding()
            .WithCancellationToken(stoppingToken)
            .Build();

        await using var messages = await Fixture.Client.Streams
            .ReadRecords(options);

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

        await Assert.That(records.Count).IsEqualTo(simulation.Records.Count);
    }

    [Test]
    public async ValueTask reads_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var now = Fixture.Time.GetUtcNow();

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsEqualTo(simulation.Records.Count);

        for (var i = 0; i < records.Count; i++) {
            var record   = records[i];
            var iotEvent = simulation.Records[i];

            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
            await Assert.That(record.Revision).IsEquivalentTo(Revision.From(i));
            await Assert.That(record.Position.LogPosition).IsLessThanOrEqualTo(simulation.Position);

            await Assert.That(record.Schema.SchemaName.Value).IsNotEmpty();
            await Assert.That(record.Schema.SchemaFormat).IsEqualTo(iotEvent.DataFormat);
            await Assert.That(record.Schema.SchemaVersionId).IsEqualTo(SchemaVersionId.Unset);

            await Assert.That(record.Value).IsEquivalentTo(iotEvent.Value);
            await Assert.That(record.ValueType).IsEqualTo(iotEvent.Value.GetType());

            await Assert.That(record.Timestamp < now.DateTime || record.Timestamp > Fixture.Time.GetUtcNow().DateTime).IsTrue();

            await Assert.That(record.Metadata.Stringify()).IsEquivalentTo(iotEvent.Metadata.Stringify());
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
            .ReadStreamRecords(simulation.Stream, cancellator.Token);

        var cancelOn = simulation.Records.Count / 2 + simulation.Records.Count % 2;

        await foreach (var _ in messages)
            if (--cancelOn == 0 && !cancellator.IsCancellationRequested)
                await cancellator.CancelAsync();

        await Assert.That(messages.QueuedMessages).IsEqualTo(0);
        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
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
            .ReadStreamRecords(simulation.Stream, ct);

        var cancelOn = simulation.Records.Count / 2 + simulation.Records.Count % 2;

        using var cancellator = new CancellationTokenSource();

        await foreach (var _ in messages.WithCancellation(cancellator.Token)) {
            if (--cancelOn == 0)
                cancellator.Cancel();
            else
                await Task.Delay(25); // simulate some processing time
        }

        await Assert.That(messages.QueuedMessages).IsGreaterThanOrEqualTo(cancelOn);

        await Assert.That(cancellator.Token.IsCancellationRequested).IsTrue();
        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
    }

    [Test]
    public async ValueTask reads_first_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var expectedRecord = simulation.Records.First();

        var record = await Fixture.Client.Streams
            .ReadFirstStreamRecord(simulation.Stream, ct);

        await Assert.That(record.Value).IsEquivalentTo(expectedRecord.Value);
        await Assert.That(record.Id).IsEqualTo(expectedRecord.RecordId);
    }

    [Test]
    public async ValueTask reads_last_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var expectedRecord = simulation.Records.Last();

        var record = await Fixture.Client.Streams
            .ReadLastStreamRecord(simulation.Stream, ct);

        await Assert.That(record.Value).IsEquivalentTo(expectedRecord.Value);
        await Assert.That(record.Id).IsEqualTo(expectedRecord.RecordId);
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
            .ReadStreamRecords(simulation.Stream, ct);

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

        var ex = await Assert.ThrowsAsync<LogPositionNotFoundException>(
            async () => await Fixture.Client.Streams
                .ReadRecords(brokenPosition, ct));

        await Assert.That(ex.Message).Contains($"{brokenPosition}");
    }

    [Test]
    public async ValueTask reads_stream_from_specific_revision(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(10, ct);

        var startFrom = 5;

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, Revision.From(startFrom), ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var expectedRecords = simulation.Records.Skip(startFrom).ToList();

        await Assert.That(records.Count).IsEqualTo(expectedRecords.Count);

        for (var i = 0; i < records.Count; i++) {
            var record   = records[i];
            var expected = expectedRecords[i];

            await Assert.That(record.Stream).IsEqualTo(simulation.Stream);
            await Assert.That(record.Revision).IsEquivalentTo(Revision.From(startFrom + i));
            await Assert.That(record.Id).IsEqualTo(expected.RecordId);
            await Assert.That(record.Value).IsEquivalentTo(expected.Value);
        }
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_is_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct))
            .Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_is_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHomeActivity();

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(activity.Stream, ct))
            .Throws<StreamNotFoundException>();
    }
}
