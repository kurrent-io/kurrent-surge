using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Testkit;
using Sdk.TestKit.Fixtures;
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
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stoppingToken = cancellator.Token;

        var lastPosition = await Fixture.LatestRecordPosition(stoppingToken);

        var simulation = await Fixture.SeedTestStream(10, stoppingToken);

        var options = ReadLog.FromStart()
            .WithFilterExpression($"stream = '{simulation.Stream}'")
            .SkipDecoding()
            .WithCancellationToken(stoppingToken)
            .From(lastPosition.LogPosition)
            .Build();

        await using var messages = await Fixture.Client.Streams.ReadRecords(options);

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

        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);
    }

    [Test]
    public async ValueTask reads_stream(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var now = Fixture.Time.GetUtcNow();

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        await Assert.That(records.Count).IsEqualTo(data.Records.Length);

        for (var i = 0; i < records.Count; i++) {
            var record   = records[i];
            var iotEvent = data.Records[i];

            await Assert.That(record.Stream).IsEqualTo(data.Stream);
            await Assert.That(record.Revision).IsEquivalentTo(Revision.From(i));
            await Assert.That(record.Position.LogPosition).IsLessThanOrEqualTo(data.Position);

            await Assert.That(record.Schema.SchemaName.Value).IsNotEmpty();
            await Assert.That(record.Schema.SchemaFormat).IsEqualTo(iotEvent.DataFormat);
            await Assert.That(record.Schema.SchemaVersionId).IsNotEqualTo(SchemaVersionId.Unset);

            await Assert.That(record.Value).IsEquivalentTo(iotEvent.Value);
            await Assert.That(record.ValueType).IsEqualTo(iotEvent.Value.GetType());

            await Assert.That(record.Timestamp < now.DateTime || record.Timestamp > Fixture.Time.GetUtcNow().DateTime).IsTrue();

            await Assert.That(record.Metadata.Stringify()).IsEquivalentTo(iotEvent.Metadata.Stringify());
        }
    }

    [Test]
    [Timeout(25000)]
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
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var data = await Fixture.SeedTestStream(records, cancellator.Token);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, cancellator.Token);

        var cancelOn = (data.Records.Length / 2) + (data.Records.Length % 2);

        await foreach (var _ in messages)
            if (--cancelOn == 0 && !cancellator.IsCancellationRequested)
                await cancellator.CancelAsync();

        await Assert.That(messages.QueuedMessages).IsEqualTo(0);
        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
    }

    [Test]
    [Timeout(25000)]
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
        using var cancellator =  CancellationTokenSource.CreateLinkedTokenSource(ct);

        var data = await Fixture.SeedTestStream(records, cancellator.Token);

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, cancellator.Token);

        var cancelOn = (data.Records.Length / 2) + (data.Records.Length % 2);

        await foreach (var _ in messages.WithCancellation(cancellator.Token)) {
            if (--cancelOn == 0)
                await cancellator.CancelAsync();
            else if (!cancellator.IsCancellationRequested)
                await Task.Delay(25, cancellator.Token); // simulate some processing time
        }

        await Assert.That(messages.QueuedMessages).IsGreaterThanOrEqualTo(cancelOn);
        await Assert.That(cancellator.Token.IsCancellationRequested).IsTrue();
        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
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

        // wtf?!
        // error message:
        // "The provided log position does not exist: 18446744073709550616.
        // Details: Error while reading log record forwards at actual position 23780042.
        // Ticks must be between DateTime.MinValue.Ticks and DateTime.MaxValue.Ticks. (Parameter 'ticks')"

        // error message:
        // "The provided log position does not exist: 18446744073709550616.
        // Details: There is not enough space to read full record (length prefix: 464640).
        // Actual pre-position: 30628457. Something is seriously wrong in chunk #2-2 (chunk-000002.000000).")"

        var simulation = await Fixture.SeedTestStream(ct);

        var messages = await Fixture.Client.Streams
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
        var seedData = await Fixture.SeedTestStream(10, ct);

        var startFrom = 5;

        await using var messages = await Fixture.Client.Streams
            .ReadStreamRecords(seedData.Stream, startFrom, ct);

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        var expectedRecords = seedData.Records.Skip(startFrom).ToList();

        await Assert.That(records.Count).IsEqualTo(expectedRecords.Count);

        for (var i = 0; i < records.Count; i++) {
            var record   = records[i];
            var expected = expectedRecords[i];

            await Assert.That(record.Id).IsDeepEquivalentTo(expected.RecordId);
            await Assert.That(record.Value).IsDeepEquivalentTo(expected.Value);
            await Assert.That(record.Stream).IsDeepEquivalentTo(seedData.Stream);
            await Assert.That(record.Revision).IsDeepEquivalentTo(startFrom + i);
            await Assert.That(startFrom + i).IsDeepEquivalentTo(record.Revision);

        }
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_is_deleted(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams
            .Delete(data.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct))
            .Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_is_tombstoned(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams
            .Tombstone(data.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .ReadStreamRecords(data.Stream, ct))
            .Throws<StreamNotFoundException>();
    }
}
