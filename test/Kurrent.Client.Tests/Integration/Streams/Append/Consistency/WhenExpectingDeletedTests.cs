using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class WhenExpectingDeletedTests : AppendConsistencyTestBase {
    [Test]
    public async ValueTask succeeds_when_stream_is_deleted(CancellationToken ct) {
        // arrange
        var (checkedStream, _, _) = await SeedDeletedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .WithRecords(data.Records.ToArray())
            .WithCheck(ConsistencyCheck.Stream(checkedStream).IsDeleted())
            .Build();

        // act
        var result = await Fixture.Client.Streams.AppendRecords(req, ct);

        // assert
        await Assert.That(result.Revisions[0].Revision).IsEqualTo(data.Records.Count - 1);
    }

    [Test]
    public async ValueTask fails_when_stream_is_active(CancellationToken ct) {
        // arrange
        var (checkedStream, revision, _) = await SeedActiveStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .WithRecords(data.Records.ToArray())
            .WithCheck(ConsistencyCheck.Stream(checkedStream).IsDeleted())
            .Build();

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(req, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo((ActualStreamState)revision);
    }

    [Test]
    public async ValueTask fails_when_stream_does_not_exist(CancellationToken ct) {
        // arrange
        var checkedStream = NewNonExistentStreamName();
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .WithRecords(data.Records.ToArray())
            .WithCheck(ConsistencyCheck.Stream(checkedStream).IsDeleted())
            .Build();

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(req, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.NoStream);
    }

    [Test]
    public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
        // arrange
        var (checkedStream, _, _) = await SeedTombstonedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var req = AppendRecordsRequest.New()
            .WithRecords(data.Records.ToArray())
            .WithCheck(ConsistencyCheck.Stream(checkedStream).IsDeleted())
            .Build();

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(req, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.Tombstoned);
    }
}
