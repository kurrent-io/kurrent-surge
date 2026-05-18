using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class WhenExpectingRevisionTests : AppendConsistencyTestBase {
    [Test]
    public async ValueTask succeeds_when_stream_at_matching_revision(CancellationToken ct) {
        // arrange
        var (checkedStream, revision, _) = await SeedActiveStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .AddRecords(data.Records.ToArray())
            .AddCheck(ConsistencyCheck.StreamAtRevision(checkedStream, revision))
            .Build();

        // act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // assert
        await Assert.That(result.Revisions[0].Revision).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask succeeds_when_stream_is_deleted(CancellationToken ct) {
        // arrange
        var (checkedStream, revision, _) = await SeedDeletedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .AddRecords(data.Records.ToArray())
            .AddCheck(ConsistencyCheck.StreamAtRevision(checkedStream, revision))
            .Build();

        // act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // assert
        await Assert.That(result.Revisions[0].Revision).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask fails_when_stream_does_not_exist(CancellationToken ct) {
        // arrange
        var checkedStream    = NewNonExistentStreamName();
        var expectedRevision = Revision.From(0);
        var data             = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .AddRecords(data.Records.ToArray())
            .AddCheck(ConsistencyCheck.StreamAtRevision(checkedStream, expectedRevision))
            .Build();

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(req, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(expectedRevision);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.NoStream);
    }

    [Test]
    public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
        // arrange
        var (checkedStream, revision, _) = await SeedTombstonedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .AddRecords(data.Records.ToArray())
            .AddCheck(ConsistencyCheck.StreamAtRevision(checkedStream, revision))
            .Build();

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(req, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(revision);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.Tombstoned);
    }
}
