using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams"), Category("Management"), Category("Tombstone")]
public class TombstoneTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask tombstones_existing_stream_without_specifing_revision(CancellationToken ct) {
        var smartHomeActivity = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(smartHomeActivity.Stream, ct)
            .ShouldNotThrowOrFailAsync(
                position => position.ShouldBeGreaterThanOrEqualTo(smartHomeActivity.Position));
    }

    [Test]
    public async ValueTask tombstones_stream_when_expected_revision_matches(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, simulation.StreamRevision, ct)
            .ShouldNotThrowOrFailAsync(position =>
                position.ShouldBeGreaterThanOrEqualTo(simulation.Position));
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, simulation.StreamRevision + 99, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(TombstoneStreamError.TombstoneStreamErrorCase.StreamRevisionConflictException));
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_not_found_error_when_stream_does_not_exist(CancellationToken ct) {
        var simulation = HomeAutomationTestData.SimulateHomeActivity();

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(TombstoneStreamError.TombstoneStreamErrorCase.StreamNotFoundException));
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_stream_deleted_error_when_stream_was_deleted(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Delete(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(TombstoneStreamError.TombstoneStreamErrorCase.StreamDeletedException));
    }

    [Test]
    public async ValueTask fails_to_tombstone_stream_with_stream_tombstoned_error_when_stream_was_tombstoned(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Streams
            .Tombstone(simulation.Stream, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(TombstoneStreamError.TombstoneStreamErrorCase.StreamTombstonedException));
    }
}
