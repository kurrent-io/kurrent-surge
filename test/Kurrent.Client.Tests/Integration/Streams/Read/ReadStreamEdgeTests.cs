using Kurrent.Client.Exceptions;
using Kurrent.Client.Streams;
using Kurrent.Testkit;
using Sdk.TestKit.Fixtures;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(10_000)]
public class ReadStreamEdgeTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    public class ReadStreamEdgeTestCases : TestCaseDataGenerator<(ReadDirection Direction, bool SkipDecoding)> {
        protected override IEnumerable<TestCase<(ReadDirection Direction, bool SkipDecoding)>> Data() {
            yield return new ((ReadDirection.Forwards, false), DisplayName: "forwards");
            yield return new((ReadDirection.Backwards, false), DisplayName: "backwards");
            yield return new((ReadDirection.Forwards, true), DisplayName: "forwards no decoding");
            yield return new((ReadDirection.Backwards, true), DisplayName: "backwards no decoding");
        }
    }

	[Test]
    [ReadStreamEdgeTestCases]
    public async ValueTask reads_stream_edge(ReadDirection direction, bool skipDecoding, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var expectedRecordInfo = direction == ReadDirection.Forwards
			? data.Records.First()
			: data.Records.Last();

        var expectedRevision = direction == ReadDirection.Forwards
            ? Revision.Min
            : data.Revision;

		var record = await Fixture.Client.Streams
			.ReadStreamEdge(data.Stream, direction, skipDecoding: skipDecoding, ct);

        await Assert.That(record.Id).IsEqualTo(expectedRecordInfo.RecordId);
		await Assert.That(record.Revision).IsEqualTo(expectedRevision);
        await Assert.That(record.Value).IsEquivalentTo(expectedRecordInfo.Value).When(!skipDecoding);
    }

	[Test]
	[Arguments(ReadDirection.Forwards)]
	[Arguments(ReadDirection.Backwards)]
	public async ValueTask fails_when_stream_does_not_exist(ReadDirection direction, CancellationToken ct) {
		var data = await Fixture.TestData.GenerateSet(ct);

		await Assert.That(async () => await Fixture.Client.Streams
			.ReadStreamEdge(data.Stream, direction, skipDecoding: true, ct))
			.Throws<StreamNotFoundException>();
	}

	[Test]
	[Arguments(ReadDirection.Forwards)]
	[Arguments(ReadDirection.Backwards)]
	public async ValueTask fails_when_stream_is_deleted(ReadDirection direction, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Delete(data.Stream, ct);

		await Assert.That(async () => await Fixture.Client.Streams
			.ReadStreamEdge(data.Stream, direction, skipDecoding: true, ct))
			.Throws<StreamDeletedException>();
	}

	[Test]
	[Arguments(ReadDirection.Forwards)]
	[Arguments(ReadDirection.Backwards)]
	public async ValueTask fails_when_stream_is_tombstoned(ReadDirection direction, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Tombstone(data.Stream, ct);

		await Assert.That(async () => await Fixture.Client.Streams
			.ReadStreamEdge(data.Stream, direction, skipDecoding: true, ct))
			.Throws<StreamTombstonedException>();
	}

    [Test]
    public async ValueTask fails_when_concurrent_reads_on_missing_stream(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        var lastTask = Fixture.Client.Streams
            .ReadStreamEdge(data.Stream, ReadDirection.Backwards, skipDecoding: true, ct).AsTask();

        var firstTask = Fixture.Client.Streams
            .ReadStreamEdge(data.Stream, ReadDirection.Forwards, skipDecoding: true, ct).AsTask();

        await Assert.That(async () => await Task.WhenAll(lastTask, firstTask))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask succeeds_when_concurrent_reads_on_missing_stream(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var lastTask  = Fixture.Client.Streams
            .ReadStreamEdge(data.Stream, ReadDirection.Backwards, skipDecoding: true, ct).AsTask();

        var firstTask = Fixture.Client.Streams
            .ReadStreamEdge(data.Stream, ReadDirection.Forwards, skipDecoding: true, ct).AsTask();

        await Task.WhenAll(lastTask, firstTask);

        await Assert.That(lastTask).IsCompletedSuccessfully();
        await Assert.That(firstTask).IsCompletedSuccessfully();

        await Assert.That(lastTask.Result).IsNotEqualTo(Record.None);
        await Assert.That(firstTask.Result).IsNotEqualTo(Record.None);
    }

}
