using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
public class InspectRecordTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async Task inspects_log_record(CancellationToken ct) {
        var seeded = await Fixture.SeedTestStream(ct);

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
        var seeded = await Fixture.SeedTestStream(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, ct);

        await Assert.That(record.Position.LogPosition).IsLessThanOrEqualTo(seeded.Position);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_first_record_using_earliest_position(CancellationToken ct) {
        await Fixture.SeedTestStream(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Earliest, ct);

        await Assert.That(record.Position.LogPosition).IsGreaterThanOrEqualTo(0);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_last_record_using_latest_position(CancellationToken ct) {
        await Fixture.SeedTestStream(10, ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(LogPosition.Latest, ct);

        await Assert.That(record.Position.LogPosition).IsGreaterThan(0);
        await Assert.That(record.Stream).IsNotEqualTo(StreamName.Unset);
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_undecoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedTestStream(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: true, ct);

        await Assert.That(record.Data.Length).IsGreaterThan(0);
        await Assert.That(record.Value).IsNull();
        await Assert.That(record.IsDecoded).IsFalse();
    }

    [Test]
    public async ValueTask returns_decoded_record(CancellationToken ct) {
        var seeded = await Fixture.SeedTestStream(ct);

        var record = await Fixture.Client.Streams
            .InspectRecord(seeded.Position, skipDecoding: false, ct);

        await Assert.That(record.Data.Length).IsGreaterThan(0);
        await Assert.That(record.Value).IsNotNull();
        await Assert.That(record.IsDecoded).IsTrue();
    }

    [Test]
    public async ValueTask returns_record_when_source_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(ct);

        var expectedRecordId = simulation.Records.Last().RecordId;

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        // A soft-deleted source stream does not hide the physical record at its log position.
        var record = await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct);

        await Assert.That(record).IsNotEqualTo(Record.None);
        await Assert.That(record.Id).IsEqualTo(expectedRecordId);
        await Assert.That(record.Position.LogPosition).IsEqualTo(simulation.Position);
    }

    [Test]
    public async ValueTask returns_record_when_source_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(ct);

        var expectedRecordId = simulation.Records.Last().RecordId;

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        // A tombstoned source stream does not hide the physical record at its log position.
        var record = await Fixture.Client.Streams
            .InspectRecord(simulation.Position, ct);

        await Assert.That(record).IsNotEqualTo(Record.None);
        await Assert.That(record.Id).IsEqualTo(expectedRecordId);
        await Assert.That(record.Position.LogPosition).IsEqualTo(simulation.Position);
    }

    [Test]
    public async ValueTask returns_none_when_log_position_does_not_exist(CancellationToken ct) {
        var nonExistentPosition = long.MaxValue - 1000;

        var record = await Fixture.Client.Streams
            .InspectRecord(nonExistentPosition, ct);

        await Assert.That(record).IsEqualTo(Record.None);
    }

    [Test]
    public async ValueTask throws_ArgumentOutOfRangeException_when_log_position_is_unset(CancellationToken ct) {
        await Assert.That(async () => await Fixture.Client.Streams
            .InspectRecord(LogPosition.Unset, ct))
            .Throws<ArgumentOutOfRangeException>();
    }
}
