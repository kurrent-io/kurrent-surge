using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Delete")]
[Category("Integration")]
public class DeleteTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask deletes_existing_stream_without_specifing_revision(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var position = await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        await Assert.That(position).IsGreaterThanOrEqualTo(simulation.Position);
    }

    [Test]
    public async ValueTask deletes_stream_when_expected_revision_matches(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var position = await Fixture.Client.Streams
            .Delete(simulation.Stream, simulation.Revision, ct);

        await Assert.That(position).IsGreaterThanOrEqualTo(simulation.Position);
    }

    [Test]
    public async ValueTask fails_to_delete_stream_with_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var simulation  = await Fixture.SeedSmartHomeActivity(ct);
        var badRevision = simulation.Revision + 1;

        await Assert.That(async () => await Fixture.Client.Streams
            .Delete(simulation.Stream, badRevision, ct))
            .Throws<StreamRevisionConflictException>();
    }

    [Test]
    public async ValueTask fails_to_delete_stream_with_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Assert.That(async () => await Fixture.Client.Streams
            .Delete(simulation.Stream, ct))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask fails_to_delete_stream_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Delete(simulation.Stream, ct))
            .Throws<StreamDeletedException>();
    }

    [Test]
    public async ValueTask fails_to_delete_stream_with_stream_tombstoned_error_when_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct);

        await Assert.That(async () => await Fixture.Client.Streams
            .Delete(simulation.Stream, ct))
            .Throws<StreamTombstonedException>();
    }
}
