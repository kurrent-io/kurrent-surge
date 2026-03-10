using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

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
            .InspectRecord(seeded.Position, ct);

        await Assert.That(record.Id).IsEqualTo(lastRecord.RecordId);
        await Assert.That(record.Stream).IsEqualTo(seeded.Stream);
        await Assert.That(record.Position.LogPosition).IsEqualTo(seeded.Position);
        await Assert.That(record.Data.Length).IsGreaterThan(0);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_record_at_specific_position(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct);

        await Assert.That(record.Position.LogPosition).IsLessThanOrEqualTo(seeded.Position);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_first_record_using_earliest_position(CancellationToken ct) {
        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Earliest, ct);

        await Assert.That(record.Position.LogPosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_last_record_using_latest_position(CancellationToken ct) {
        //TODO SS: Investigate, sometimes throws "Object reference not set to an instance of an object."

        await Fixture.SeedSmartHomeActivity(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Latest, ct);

        await Assert.That(record.Position.LogPosition).IsGreaterThan(0);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_undecoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: true, ct);

        await Assert.That(record.Data.Length).IsGreaterThan(0);
        await Assert.That(record.Value).IsNull();
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_decoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedSmartHomeActivity(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: false, ct);

        await Assert.That(record.Data.Length).IsGreaterThan(0);
        await Assert.That(record.Value).IsNotNull();
        await Assert.That(record.IsDecoded).IsTrue();
    }

    [Test]
    public async ValueTask fails_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct))
            .Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_with_stream_tombstoned_error_when_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct))
            .Throws<StreamTombstonedException>();
    }

    [Test]
    public async ValueTask fails_with_LogPositionNotFoundException_when_log_position_does_not_exist(CancellationToken ct) {
        var nonExistentPosition = int.MaxValue - 1000;

        await Assert.That(async () => await Fixture.Client.Streams
            .InspectRecord(nonExistentPosition, ct))
            .Throws<LogPositionNotFoundException>();
    }

    [Test]
    public async ValueTask throws_ArgumentOutOfRangeException_when_log_position_is_unset() {
        var action = async () => await Fixture.Client.Streams
            .InspectRecord(LogPosition.Unset, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);
    }
}
