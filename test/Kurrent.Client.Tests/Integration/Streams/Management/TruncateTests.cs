using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Truncate")]
[Category("Integration")]
public class TruncateTests {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask truncates_stream_at_revision(CancellationToken ct) {
        var simulation = await Fixture.SeedData(ct);

        await  Fixture.Client.Streams
            .Truncate(simulation.Stream, simulation.Revision, ct);

        var messages = await  Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct);

        var count = await messages.CountAsync(ct);
        await Assert.That(count).IsEqualTo(1);
    }

    [Test]
    public async ValueTask truncates_full_stream(CancellationToken ct) {
        var simulation = await Fixture.SeedData(ct);

        var revision = simulation.Revision + 1; // all messages

        await Fixture.Client.Streams
            .Truncate(simulation.Stream, revision, ct);

        var messages = await Fixture.Client.Streams
            .ReadStreamRecords(simulation.Stream, ct);

        var count = await messages.CountAsync(ct);
        await Assert.That(count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask returns_revision_conflict_error_when_revision_is_not_matched(CancellationToken ct) {
        var simulation = await Fixture.SeedData(ct);

        var badRevision = simulation.Revision + 99;

        await Assert.That(async () => await Fixture.Client.Streams
            .Truncate(simulation.Stream, badRevision, ct))
            .Throws<StreamRevisionConflictException>();
    }
}
