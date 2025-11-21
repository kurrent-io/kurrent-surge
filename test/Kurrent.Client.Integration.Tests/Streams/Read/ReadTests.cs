using Kurrent.Client.Exceptions;
using Kurrent.Client.Registry;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;
using Kurrent.Testing.Shouldly;
using Kurrent.Testing.TUnit;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
public class ReadTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_log(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var options = new ReadOptions {
            Start  = simulation.Position,
            Filter = RecordFilter.ByExpression($"stream = '{simulation.Stream}'"),
            CancellationToken = ct
        };

        await using var messages = await Fixture.Client.Streams
            .Read(options)
            .ShouldNotThrowOrFailAsync();

        var records = await messages
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .TakeWhile(rec => rec.Position <= simulation.Position)
            .ToListAsync(ct);

        records.Count.ShouldBe(simulation.Records.Count);
    }


    [Test]
    public async ValueTask reads_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var now = Fixture.Time.GetUtcNow();

        await using var messages = await Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
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
            record.Position.ShouldBeLessThanOrEqualTo(simulation.Position);

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
    [Skip("Needs investigation")]
    public async ValueTask gracefully_stops_reading_and_flushes_queue_when_read_token_is_cancelled(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await using var messages = await Fixture.Client.Streams
            .ReadStream(simulation.Stream, cancellator.Token)
            .ShouldNotThrowOrFailAsync();

        var cancelOn = simulation.Records.Count / 2;

        await foreach (var _ in messages)
            if (--cancelOn == 0 && !cancellator.IsCancellationRequested)
                await cancellator.CancelAsync();

        messages.QueuedMessages.ShouldBe(0, "Should have flushed all messages on cancellation");
        cancellator.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    [Skip("Needs investigation")]
    public async ValueTask gracefully_stops_reading_without_flushing_queue_when_enumerable_token_is_cancelled(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await using var messages = await Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var cancelOn = simulation.Records.Count / 2;

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await foreach (var _ in messages.WithCancellation(cancellator.Token))
            if (--cancelOn == 0)
                await cancellator.CancelAsync();

        messages.QueuedMessages.ShouldBeGreaterThanOrEqualTo(
            simulation.Records.Count / 2, "Should not queue more than one message before cancellation"
        );

        cancellator.IsCancellationRequested.ShouldBeTrue();
    }

    [Test]
    public async ValueTask reads_first_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var firstMessage = simulation.Records.First();

        await Fixture.Client.Streams
            .ReadFirstStreamRecord(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(record =>
                record.Value.ShouldBeEquivalentTo(firstMessage.Value));
    }

    [Test]
    public async ValueTask reads_last_stream_record(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var lastMessage = simulation.Records.Last();

        await Fixture.Client.Streams
            .ReadLastStreamRecord(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync(record =>
                record.Value.ShouldBeEquivalentTo(lastMessage.Value));
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(ReadError.ReadErrorCase.StreamDeletedException));
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_when_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(ReadError.ReadErrorCase.StreamTombstonedException));
    }

    [Test]
    public async ValueTask fails_with_stream_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .ReadStream(activity.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(ReadError.ReadErrorCase.StreamNotFoundException));
    }

    public class NonExistingPositions : TestCaseGenerator<LogPosition> {
        protected override IEnumerable<LogPosition> Data() => [
            LogPosition.Latest, // edge case as it represents the end of the log
            LogPosition.Latest - 1
        ];
    }

    [Test, NonExistingPositions]
    public async ValueTask fails_with_log_position_not_found_when_log_position_does_not_exist(LogPosition position, CancellationToken ct) {
        await Fixture.Client.Streams
            .Read(position, ct)
            .ShouldFailAsync(error => {
                error.Value.ShouldBeOfType<LogPositionNotFoundException>()
                    .Message.ShouldContain($"{position}");
            });
    }
}
