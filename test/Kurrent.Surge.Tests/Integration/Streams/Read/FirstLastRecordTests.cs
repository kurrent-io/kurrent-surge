using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Streams;

[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(10_000)]
public class FirstLastRecordTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    public enum Edge { First, Last }

    public class FirstLastRecordTestCases : TestCaseDataGenerator<(Edge Edge, bool SkipDecoding)> {
        protected override IEnumerable<TestCase<(Edge Edge, bool SkipDecoding)>> Data() {
            yield return new((Edge.First, false), DisplayName: "first");
            yield return new((Edge.Last, false), DisplayName: "last");
            yield return new((Edge.First, true), DisplayName: "first no decoding");
            yield return new((Edge.Last, true), DisplayName: "last no decoding");
        }
    }

    ValueTask<Record> ReadEdge(Edge edge, StreamName stream, bool skipDecoding, CancellationToken ct) =>
        edge == Edge.First
            ? Fixture.Client.Streams.FirstRecord(stream, skipDecoding, ct)
            : Fixture.Client.Streams.LastRecord(stream, skipDecoding, ct);

	[Test]
    [FirstLastRecordTestCases]
    public async ValueTask reads_first_or_last_record_of_stream(Edge edge, bool skipDecoding, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		var expectedRecordInfo = edge == Edge.First
			? data.Records.First()
			: data.Records.Last();

        var expectedRevision = edge == Edge.First
            ? Revision.Min
            : data.Revision;

		var record = await ReadEdge(edge, data.Stream, skipDecoding, ct);

        await Assert.That(record.Id).IsEqualTo(expectedRecordInfo.RecordId);
		await Assert.That(record.Revision).IsEqualTo(expectedRevision);
        await Assert.That(record.Value).IsEquivalentTo(expectedRecordInfo.Value).When(!skipDecoding);
    }

    [Test]
    public async ValueTask reads_first_record_of_log(CancellationToken ct) {
        await Fixture.SeedTestStream(ct);

        var record = await Fixture.Client.Streams.FirstRecord(StreamName.Log, skipDecoding: true, ct);

        await Assert.That(record).IsNotEqualTo(Record.None);
    }

    [Test]
    public async ValueTask reads_last_record_of_log(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var record = await Fixture.Client.Streams.LastRecord(StreamName.Log, skipDecoding: true, ct);

        // No position comparison against the seeded append: in a shared, parallel container the log
        // tail can be any concurrent writer's record. Link resolution is off, so the returned record
        // is the physical record at the tail, and a soft-deleted or tombstoned tail stream no longer
        // makes this throw. The durable invariant is only that a log-targeted read reaches a real
        // record at the tail.
        await Assert.That(record).IsNotEqualTo(Record.None);
        await Assert.That(record.Position.LogPosition.IsUnset).IsFalse();
    }

	[Test]
	[Arguments(Edge.First)]
	[Arguments(Edge.Last)]
	public async ValueTask fails_when_stream_does_not_exist(Edge edge, CancellationToken ct) {
		var data = await Fixture.TestData.GenerateSet(ct);

		await Assert.That(async () => await ReadEdge(edge, data.Stream, skipDecoding: true, ct))
			.Throws<StreamNotFoundException>();
	}

	[Test]
	[Arguments(Edge.First)]
	[Arguments(Edge.Last)]
	public async ValueTask fails_when_stream_is_deleted(Edge edge, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Delete(data.Stream, ct);

		await Assert.That(async () => await ReadEdge(edge, data.Stream, skipDecoding: true, ct))
			.Throws<StreamDeletedException>();
	}

	[Test]
	[Arguments(Edge.First)]
	[Arguments(Edge.Last)]
	public async ValueTask fails_when_stream_is_tombstoned(Edge edge, CancellationToken ct) {
		var data = await Fixture.SeedTestStream(ct);

		await Fixture.Client.Streams.Tombstone(data.Stream, ct);

		await Assert.That(async () => await ReadEdge(edge, data.Stream, skipDecoding: true, ct))
			.Throws<StreamTombstonedException>();
	}

    [Test]
    [Arguments(Edge.First)]
    [Arguments(Edge.Last)]
    public async ValueTask returns_none_when_stream_exists_but_has_no_records(Edge edge, CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        // Truncate away every record: the stream still exists (not deleted), but the edge read finds
        // no readable record, so it must return Record.None instead of throwing.
        await Fixture.Client.Streams.Truncate(data.Stream, data.Revision + 1, ct);

        var record = await ReadEdge(edge, data.Stream, skipDecoding: true, ct);

        await Assert.That(record).IsEqualTo(Record.None);
    }

    [Test]
    public async ValueTask fails_when_concurrent_reads_on_missing_stream(CancellationToken ct) {
        var data = await Fixture.TestData.GenerateSet(ct);

        var lastTask = Fixture.Client.Streams
            .LastRecord(data.Stream, skipDecoding: true, ct).AsTask();

        var firstTask = Fixture.Client.Streams
            .FirstRecord(data.Stream, skipDecoding: true, ct).AsTask();

        await Assert.That(async () => await Task.WhenAll(lastTask, firstTask))
            .Throws<StreamNotFoundException>();
    }

    [Test]
    public async ValueTask succeeds_when_concurrent_reads_on_existing_stream(CancellationToken ct) {
        var data = await Fixture.SeedTestStream(ct);

        var lastTask  = Fixture.Client.Streams
            .LastRecord(data.Stream, skipDecoding: true, ct).AsTask();

        var firstTask = Fixture.Client.Streams
            .FirstRecord(data.Stream, skipDecoding: true, ct).AsTask();

        await Task.WhenAll(lastTask, firstTask);

        await Assert.That(lastTask).IsCompletedSuccessfully();
        await Assert.That(firstTask).IsCompletedSuccessfully();

        await Assert.That(lastTask.Result).IsNotEqualTo(Record.None);
        await Assert.That(firstTask.Result).IsNotEqualTo(Record.None);
    }

}
