using JetBrains.Annotations;
using Kurrent.Client.Streams;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Client.Tests.Tools;

namespace Kurrent.Client.Tests.Streams;

public abstract class AppendConsistencyTestBase {
    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    protected async ValueTask<(StreamName Stream, Revision Revision)> SeedActiveStream(
        int records, CancellationToken ct) {
        var activity = HomeAutomationTestData.SimulateHomeActivity(records);
        var result   = await Fixture.Client.Streams.AppendRecords(activity.AppendRequest, ct);
        return (activity.Stream, result.Revisions[0].Revision);
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
            .WithRecord(new { Type = "consistency-test" })
            .Build();
}
