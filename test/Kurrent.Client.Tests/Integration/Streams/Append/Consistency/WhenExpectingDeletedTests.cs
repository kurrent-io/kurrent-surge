using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Integration")]
public class WhenExpectingDeletedTests : AppendConsistencyTestBase {
    [Test]
    public async ValueTask fails_when_stream_is_active(CancellationToken ct) {
        var (stream, revision) = await SeedActiveStream(3, ct);

        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(
                BuildAppendRequest(stream, ExpectedStreamState.Deleted), ct));

        var violation = ex.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo((ActualStreamState)revision);
    }

    [Test]
    public async ValueTask fails_when_stream_does_not_exist(CancellationToken ct) {
        var stream = NewNonExistentStreamName();

        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(
                BuildAppendRequest(stream, ExpectedStreamState.Deleted), ct));

        var violation = ex.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.NoStream);
    }

    [Test]
    public async ValueTask succeeds_when_stream_is_deleted(CancellationToken ct) {
        var (stream, _) = await SeedDeletedStream(3, ct);

        var result = await Fixture.Client.Streams.AppendRecords(
            BuildAppendRequest(stream, ExpectedStreamState.Deleted), ct);

        await Assert.That(result.Position).IsGreaterThan(LogPosition.Earliest);
        await Assert.That(result.Revisions[0].Revision).IsGreaterThanOrEqualTo(Revision.From(0));
    }

    [Test]
    public async ValueTask fails_when_stream_is_tombstoned(CancellationToken ct) {
        var (stream, _) = await SeedTombstonedStream(3, ct);

        var ex = await Assert.ThrowsAsync<AppendConsistencyViolationException>(
            async () => await Fixture.Client.Streams.AppendRecords(
                BuildAppendRequest(stream, ExpectedStreamState.Deleted), ct));

        var violation = ex.Violations[0].AsStreamStateViolation;
        await Assert.That(violation.ExpectedState).IsEqualTo(ExpectedStreamState.Deleted);
        await Assert.That(violation.ActualState).IsEqualTo(ActualStreamState.Tombstoned);
    }
}
