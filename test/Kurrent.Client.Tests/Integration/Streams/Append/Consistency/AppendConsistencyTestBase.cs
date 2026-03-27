using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Streams;

public abstract class AppendConsistencyTestBase {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    protected async ValueTask<(StreamName Stream, Revision Revision)> SeedActiveStream(int records, CancellationToken ct) {
        var data   = await Fixture.TestData.GenerateSet(records, ct);
        var result = await Fixture.Client.Streams.AppendToStream(
            AppendToStreamRequest.For(data.Stream).WithRecords(data.Records).Build(), ct);
        return (data.Stream, result.Revision);
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
        StreamName stream, ExpectedStreamState expectedState) {
        var builder = AppendRecordsRequest.New()
            .WithRecord(b => b.ForStream(stream).WithValue(new ConsistencyTestRecord("consistency-test")));

        if (expectedState != ExpectedStreamState.Any)
            builder.WithCheck(new StreamStateCheck(stream, expectedState));

        return builder.Build();
    }

    record ConsistencyTestRecord(string Type);
}
