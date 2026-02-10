using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
public class InspectRecordTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async Task inspects_log_record(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(ct);

        var lastRecord = seeded.Records.Last();

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct)
            .ShouldNotThrowOrFailAsync();

        record.Id.ShouldBe(lastRecord.RecordId);
        record.Stream.ShouldBe(seeded.Stream);
        record.Position.LogPosition.ShouldBe(seeded.Position);
        record.Data.Length.ShouldBeGreaterThan(0);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask returns_record_at_specific_position(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.LogPosition.ShouldBeLessThanOrEqualTo(seeded.Position);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask returns_first_record_using_earliest_position(CancellationToken ct) {
        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Earliest, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.LogPosition.ShouldBeGreaterThanOrEqualTo(0);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask returns_last_record_using_latest_position(CancellationToken ct) {
        //TODO SS: Investigate, sometimes throws "Object reference not set to an instance of an object."

        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Latest, ct)
            .ShouldNotThrowOrFailAsync();

        record.Position.LogPosition.ShouldBeGreaterThan(0);
        record.Stream.ShouldNotBe(StreamName.None);
        record.IsDecoded.ShouldBeFalse();
    }

    [Test]
    public async ValueTask returns_undecoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: true, ct)
            .ShouldNotThrowOrFailAsync();

        record.Data.Length.ShouldBeGreaterThan(0, "Data must still be present when decoding is skipped");
        record.Value.ShouldBeNull("Value should not have been decoded from data");
        record.IsDecoded.ShouldBeFalse("Record should not have been decoded");
    }

    [Test]
    public async ValueTask returns_decoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: false, ct)
            .ShouldNotThrowOrFailAsync();

        record.Data.Length.ShouldBeGreaterThan(0, "Data must still be present after decoding");
        record.Value.ShouldNotBeNull("Value should have been decoded from data");
        record.IsDecoded.ShouldBeTrue("Record should have been decoded");
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct)
            .InspectAsync(record => {
                Fixture.Logger.LogInformation("Inspected record at position {Position} from deleted stream {Stream}", record.Position, record.Stream);
            })
            .ShouldFailAsync(error => error.Value.ShouldBeOfType<StreamDeletedException>());
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct)
            .InspectAsync(record => {
                var pos = simulation.Position;
                Fixture.Logger.LogInformation("Inspected record at position {Position} from tombstoned stream {Stream}", record.Position, record.Stream);
            })
            .ShouldFailAsync(error => error.Value.ShouldBeOfType<StreamTombstonedException>());
    }

    [Test]
    public async ValueTask fails_with_LogPositionNotFoundException_when_log_position_does_not_exist(CancellationToken ct) {
        var nonExistentPosition = int.MaxValue - 1000;

        await Fixture.Client.Streams
            .InspectRecord(nonExistentPosition, ct)
            .ShouldFailAsync(error => error.Value.ShouldBeOfType<LogPositionNotFoundException>());
    }

    [Test]
    public async ValueTask throws_ArgumentOutOfRangeException_when_log_position_is_unset() {
        var action = async () => await Fixture.Client.Streams
            .InspectRecord(LogPosition.Unset, CancellationToken.None)
            .UnwrapAsync()
            .ConfigureAwait(false);

        await action.ShouldThrowAsync<ArgumentOutOfRangeException>();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);
    }
}
