using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class WhenExpectingExistsTests : AppendConsistencyTestBase {
    [Test]
    public async ValueTask succeeds_when_stream_is_active(CancellationToken ct) {
        // arrange
        var (checkedStream, _, _) = await SeedActiveStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var options = new AppendRecordsOptions()
            .AddChecks(ConsistencyCheck.StreamExists(checkedStream));

        // act
        var result = await Fixture.Client.Streams.AppendRecords(data.Records, options, ct);

        // assert
        await Assert.That(result.Revisions[0].Revision).IsEqualTo(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask fails_when_stream_does_not_exist(CancellationToken ct) {
        // arrange
        var checkedStream = NewNonExistentStreamName();
        var data = await Fixture.TestData.GenerateSet(ct);

        var options = new AppendRecordsOptions()
            .AddChecks(ConsistencyCheck.StreamExists(checkedStream));

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(data.Records, options, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.NoStream);
    }

    [Test]
    public async ValueTask fails_when_stream_is_deleted(CancellationToken ct) {
        // arrange
        var (checkedStream, _, _) = await SeedDeletedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var options = new AppendRecordsOptions()
            .AddChecks(ConsistencyCheck.StreamExists(checkedStream));

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(data.Records, options, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.Deleted);
    }

    [Test]
    public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
        // arrange
        var (checkedStream, _, _) = await SeedTombstonedStream(ct);
        var data = await Fixture.TestData.GenerateSet(ct);

        var options = new AppendRecordsOptions()
            .AddChecks(ConsistencyCheck.StreamExists(checkedStream));

        // act
        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(data.Records, options, ct));

        // assert
        var violation = ex!.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Exists);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.Tombstoned);
    }
}
