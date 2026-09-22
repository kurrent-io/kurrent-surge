using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;
using Serilog;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(10_000)]
public class ReadTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_log(CancellationToken ct) {
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var stoppingToken = cancellator.Token;

        var lastPosition = await Fixture.LatestRecordPosition(stoppingToken);

        var simulation = await Fixture.SeedTestStream(10, stoppingToken);

        var options = new ReadRecordsOptions {
            Start        = lastPosition.LogPosition,
            // LIKE keeps this on the LOG path ($all + StreamRegexFilter); equality would route to a stream read.
            Filter       = RecordFilter.ByExpression($"stream LIKE '{simulation.Stream}%'"),
            SkipDecoding = true
        };

        var messages = Fixture.Client.Streams.ReadRecords(options, stoppingToken);

        var records = new List<Record>();

        await foreach (var msg in messages.WithCancellation(stoppingToken)) {
            msg.Switch(
                record => {
                    records.Add(record);

                    Log.Information(
                        "Record {RecordId} {SchemaName} @ {Position}",
                        record.Id, record.Schema.Schema, record.Position
                    );

                    if (record.Position.LogPosition >= simulation.Position)
                        // ReSharper disable once AccessToDisposedClosure
                        cancellator.Cancel();
                },
                checkpoint => Log.Information("Checkpoint {Type} @ {Positions}", checkpoint.GetType().Name, checkpoint.Positions)
            );
        }

        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);
    }

    [Test]
    public async ValueTask reading_forwards_from_the_end_of_a_stream_returns_no_records(CancellationToken ct) {
        // Arrange — a populated stream. Revision.Max is the end; reading forwards from the end
        // reads nothing. This must be an empty success, not a throw (parity with $all End+Forwards).
        var data = await Fixture.SeedTestStream(ct);

        // Act
        var records = await Fixture.Client.Streams
            .ReadStream(data.Stream, Revision.Max, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records).IsEmpty();
    }

    [Test]
    public async ValueTask reading_a_stream_backwards_without_a_position_reads_from_the_end(CancellationToken ct) {
        // Arrange — no Start given + Backwards must default to the END (Revision.Max) and read the
        // stream in reverse. Defaulting to Min (the bug) reads only revision 0.
        var data              = await Fixture.SeedTestStream(ct);
        var expectedCount     = data.Records.Length;
        Revision lastRevision = expectedCount - 1; // 0-based revisions → last is count - 1

        // Act
        var records = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { Direction = ReadDirection.Backwards }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — from the end: full stream, first record back is the last revision
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Position.Revision).IsEqualTo(lastRevision);
    }

    [Test]
    public async ValueTask reading_records_forwards_without_a_position_reads_from_the_beginning(CancellationToken ct) {
        // Arrange — no Start given + Forwards must default to the BEGINNING (Earliest) and read the
        // log forwards. Defaulting to Latest (the bug) reads nothing. A LIKE filter keeps this on
        // the LOG path ($all + StreamRegexFilter); equality would route to a direct stream read.
        var data          = await Fixture.SeedTestStream(ct);
        var expectedCount = data.Records.Length;

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions { Filter = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'") }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — from the beginning: full history, first record is revision Min
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Position.Revision).IsEqualTo(Revision.Min);
    }

    [Test]
    public async ValueTask reading_records_backwards_without_a_position_reads_from_the_end(CancellationToken ct) {
        // Arrange — no Start given + Backwards must default to the END (Latest) and read the log in
        // reverse. A LIKE filter keeps this on the LOG path ($all + StreamRegexFilter); equality
        // would route to a direct stream read. The first record back is the stream's last.
        var data              = await Fixture.SeedTestStream(ct);
        var expectedCount     = data.Records.Length;
        Revision lastRevision = expectedCount - 1; // 0-based revisions → last is count - 1

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Filter    = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
                Direction = ReadDirection.Backwards
            }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — from the end: full history, first record back is the last revision
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Position.Revision).IsEqualTo(lastRevision);
    }

    [Test]
    public async ValueTask reading_a_stream_forwards_without_a_position_reads_from_the_beginning(CancellationToken ct) {
        // Arrange — no Start given + Forwards (default) must read from the BEGINNING (revision Min).
        var data          = await Fixture.SeedTestStream(ct);
        var expectedCount = data.Records.Length;

        // Act
        var records = await Fixture.Client.Streams
            .ReadStream(data.Stream, new ReadStreamOptions { Direction = ReadDirection.Forwards }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert — full stream, first record is revision Min
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Position.Revision).IsEqualTo(Revision.Min);
    }

    [Test]
    public async ValueTask reads_stream(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var now = Fixture.Time.GetUtcNow();

        var messages = Fixture.Client.Streams
            .ReadStream(data.Stream, ct);

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

            await Assert.That(record.Schema.Schema.Value).IsNotEmpty();
            // The seeded records state no format, and a race event is a plain type, so the
            // write path settles them to Json.
            await Assert.That(record.Schema.DataFormat).IsEqualTo(SchemaDataFormat.Json);
            await Assert.That(record.Schema.VersionId).IsNotEqualTo(SchemaVersionId.Unset);

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
    public async ValueTask gracefully_stops_reading_when_read_token_is_cancelled(int records, CancellationToken ct) {
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var data = await Fixture.SeedTestStream(records, cancellator.Token);

        var messages = Fixture.Client.Streams
            .ReadStream(data.Stream, cancellator.Token);

        var cancelOn = (data.Records.Length / 2) + (data.Records.Length % 2);
        var read     = 0;

        await foreach (var _ in messages) {
            read++;
            if (--cancelOn == 0 && !cancellator.IsCancellationRequested)
                await cancellator.CancelAsync();
        }

        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
        await Assert.That(read).IsGreaterThan(0);
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
    public async ValueTask gracefully_stops_reading_when_enumerable_token_is_cancelled(int records, CancellationToken ct) {
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var data = await Fixture.SeedTestStream(records, cancellator.Token);

        var messages = Fixture.Client.Streams
            .ReadStream(data.Stream, cancellator.Token);

        var cancelOn = (data.Records.Length / 2) + (data.Records.Length % 2);
        var read     = 0;

        await foreach (var _ in messages.WithCancellation(cancellator.Token)) {
            read++;
            if (--cancelOn == 0)
                await cancellator.CancelAsync();
        }

        await Assert.That(cancellator.IsCancellationRequested).IsTrue();
        await Assert.That(read).IsGreaterThan(0);
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

        var messages = Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct);

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
            async () => {
                await foreach (var _ in Fixture.Client.Streams.ReadRecords(brokenPosition, ct)) { }
            });

        await Assert.That(ex.Message).Contains($"{brokenPosition}");
    }

    [Test]
    public async ValueTask reads_stream_from_specific_revision(CancellationToken ct) {
        var seedData = await Fixture.SeedTestStream(10, ct);

        var startFrom = 5;

        var messages = Fixture.Client.Streams
            .ReadStream(seedData.Stream, startFrom, ct);

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

        await Assert.That(async () => {
            await foreach (var _ in Fixture.Client.Streams.ReadStream(data.Stream, ct)) { }
        }).Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_is_tombstoned(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams
            .Tombstone(data.Stream, ct);

        await Assert.That(async () => {
            await foreach (var _ in Fixture.Client.Streams.ReadStream(data.Stream, ct)) { }
        }).Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        await Assert.That(async () => {
            await foreach (var _ in Fixture.Client.Streams.ReadStream(data.Stream, ct)) { }
        }).Throws<StreamNotFoundException>();
    }

    #region . Unresolved Read Behavior .

    [Test]
    public async ValueTask unmapped_records_pass_through_raw_by_default(CancellationToken ct) {
        // Arrange — seed with the fixture's fully-mapped client, read with a client that has
        // no catalogs at all: every schema name is unmapped for the reader.
        var simulation = await Fixture.SeedTestStream(ct);

        await using var blindClient = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
        });

        var options = new ReadStreamOptions { Limit = simulation.Records.Length };

        // Act
        var records = new List<Record>();

        await foreach (var msg in blindClient.Streams.ReadStream(simulation.Stream, options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        // Assert — records arrive raw: present, undecoded, payload intact.
        await Assert.That(records.Count).IsEqualTo(simulation.Records.Length);

        foreach (var record in records) {
            await Assert.That(record.IsDecoded).IsFalse();
            await Assert.That(record.Data.IsEmpty).IsFalse();
        }
    }

    [Test]
    public async ValueTask unmapped_records_fail_the_read_when_throw_on_unresolved(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedTestStream(ct);

        await using var blindClient = KurrentClient.Create(Fixture.ConnectionString, opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
        });

        var options = new ReadStreamOptions {
            Limit             = simulation.Records.Length,
            ThrowOnUnresolved = true
        };

        // Act & Assert
        await Assert.That(async () => {
            await foreach (var _ in blindClient.Streams.ReadStream(simulation.Stream, options, ct)) { }
        }).Throws<MessageTypeResolutionException>();
    }

    #endregion
}
