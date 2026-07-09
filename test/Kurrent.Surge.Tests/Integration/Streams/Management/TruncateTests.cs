using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Truncate")]
[Category("Integration")]
public class TruncateTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask truncates_stream_at_revision(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(ct);

        await  Fixture.Client.Streams
            .Truncate(simulation.Stream, simulation.Revision, ct);

        var messages = Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct);

        var count = await messages.CountAsync(ct);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask truncates_full_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(ct);

        var revision = simulation.Revision + 1; // all messages

        await Fixture.Client.Streams
            .Truncate(simulation.Stream, revision, ct);

        var messages = Fixture.Client.Streams
            .ReadStream(simulation.Stream, ct);

        var count = await messages.CountAsync(ct);

        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask returns_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(ct);

        var badRevision = simulation.Revision + 99;

        await Assert.That(async () => await Fixture.Client.Streams
            .Truncate(simulation.Stream, badRevision, ct))
            .Throws<StreamRevisionConflictException>();
    }
}
