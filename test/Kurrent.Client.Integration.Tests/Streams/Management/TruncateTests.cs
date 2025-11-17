using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Truncate")]
[Category("Integration")]
public class TruncateTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask truncates_stream_at_revision(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        await  Fixture.Client.Streams
            .Truncate(simulation.Stream, simulation.StreamRevision, ct)
            .ShouldNotThrowOrFailAsync();

        var messages = await  Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var count = await messages.CountAsync(ct);
        count.ShouldBe(1, "Stream should contain only the truncation marker message");
    }

    [Test]
    public async ValueTask truncates_full_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var revision = simulation.StreamRevision + 1; // all messages

        await Fixture.Client.Streams
            .Truncate(simulation.Stream, revision, ct)
            .ShouldNotThrowOrFailAsync();

        var messages = await Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct)
            .ShouldNotThrowOrFailAsync();

        var count = await messages.CountAsync(ct);
        count.ShouldBe(0, "Stream should be empty after truncation");
    }

    [Test]
    public async ValueTask returns_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var simulation = await Fixture.SeedSmartHomeActivity(ct);

        var badRevision = simulation.StreamRevision + 99;

        await Fixture.Client.Streams
            .Truncate(simulation.Stream, badRevision, ct)
            .ShouldFailAsync(error =>
                error.Case.ShouldBe(TruncateStreamError.TruncateStreamErrorCase.StreamRevisionConflictException));
    }
}
