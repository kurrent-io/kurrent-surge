using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Streams;

public abstract class AppendConsistencyTestBase {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    protected async ValueTask<(StreamName Stream, Revision Revision)> SeedActiveStream(int records, CancellationToken ct) {
        var data   = await Fixture.TestData.GenerateSet(records, ct);
        var result = await Fixture.Client.Streams.AppendRecords(data.AppendRequest, ct);
        return (data.Stream, result.Revisions[0].Revision);
    }

    protected async ValueTask<(StreamName Stream, Revision Revision)> SeedDeletedStream(
        int records, CancellationToken ct) {
        var (stream, revision) = await SeedActiveStream(records, ct);
        await Fixture.Client.Streams.Delete(stream, ct);
        return (stream, revision);
    }

    protected async ValueTask<(StreamName Stream, Revision Revision)> SeedTombstonedStream(
        int records, CancellationToken ct) {
        var (stream, revision) = await SeedActiveStream(records, ct);
        await Fixture.Client.Streams.Tombstone(stream, ct);
        return (stream, revision);
    }

    protected static StreamName NewNonExistentStreamName() =>
        $"non-existent-{Guid.NewGuid():N}";

    protected static AppendRecordsRequest BuildAppendRequest(
        StreamName stream, ExpectedStreamState expectedState) =>
        AppendRecordsRequest.ToStream(stream)
            .ExpectingState(expectedState)
            .WithRecord(new ConsistencyTestRecord("consistency-test"))
            .Build();

    record ConsistencyTestRecord(string Type);
}
