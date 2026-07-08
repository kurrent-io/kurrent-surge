using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Tombstone")]
[Category("Integration")]
public class TombstoneTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask tombstones_existing_stream_without_specifing_revision(CancellationToken ct) {
        var raceActivity = await Fixture.SeedTestStream(ct);

        var position = await Fixture.Client.Streams
            .Tombstone(raceActivity.Stream, ct);

        await Assert.That(position).IsGreaterThanOrEqualTo(raceActivity.Position);
    }

    [Test]
    public async ValueTask tombstones_stream_when_expected_revision_matches(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var position = await Fixture.Client.Streams
            .Tombstone(data.Stream, data.Revision, ct);

        await Assert.That(position).IsGreaterThanOrEqualTo(data.Position);
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Tombstone(data.Stream, data.Revision + 99, ct))
            .Throws<StreamRevisionConflictException>();
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Tombstone(data.Stream, ct))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams
            .Delete(data.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Tombstone(data.Stream, ct))
            .Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_stream_tombstoned_error_when_stream_was_tombstoned(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        await Fixture.Client.Streams
            .Tombstone(data.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Tombstone(data.Stream, ct))
            .Throws<StreamTombstonedException>();
    }
}
