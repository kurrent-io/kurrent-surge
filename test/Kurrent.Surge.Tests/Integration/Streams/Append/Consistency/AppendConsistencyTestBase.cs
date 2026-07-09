using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Streams;

public abstract class AppendConsistencyTestBase {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    protected async ValueTask<(StreamName Stream, Revision Revision, LogPosition Position)> SeedActiveStream(CancellationToken ct) {
        var data   = await Fixture.TestData.GenerateSet(ct);
        var result = await Fixture.Client.Streams.AppendStream(data.Stream, data.Records, ct);
        return (data.Stream, result.Revision, result.Position.LogPosition);
    }

    protected async ValueTask<(StreamName Stream, Revision Revision, LogPosition Position)> SeedDeletedStream(CancellationToken ct) {
        var (stream, revision, position) = await SeedActiveStream(ct);
        await Fixture.Client.Streams.Delete(stream, ct);
        return (stream, revision, position);
    }

    protected async ValueTask<(StreamName Stream, Revision Revision, LogPosition Position)> SeedTombstonedStream(CancellationToken ct) {
        var (stream, revision, position) = await SeedActiveStream(ct);
        await Fixture.Client.Streams.Tombstone(stream, ct);
        return (stream, revision, position);
    }

    protected static StreamName NewNonExistentStreamName() =>
        $"non-existent-{Guid.NewGuid():N}";
}
