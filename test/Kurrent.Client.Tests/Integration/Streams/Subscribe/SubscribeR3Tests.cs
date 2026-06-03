using Kurrent.Client.Streams;
using R3;

namespace Kurrent.Client.Tests.Streams;

[Category("Streams")]
[Category("Subscribe")]
[Category("Integration")]
[Timeout(30_000)]
public class SubscribeR3Tests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask scan_computes_running_count(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — running count of records via Scan
        var counts = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .ToObservable()
            .Scan(0, (count, _) => count + 1)
            .Take(data.Records.Length)
            .ToArrayAsync(ct);

        // Assert — should be [1, 2, 3, ..., N]
        await Assert.That(counts.Length).IsEqualTo(data.Records.Length);
        await Assert.That(counts.First()).IsEqualTo(1);
        await Assert.That(counts.Last()).IsEqualTo(data.Records.Length);
    }

    [Test]
    public async ValueTask chunk_by_count_batches_records(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — batch records into chunks of 3
        var chunks = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .Chunk(3)
            .Take(3)
            .ToArrayAsync(ct);

        // Assert — 3 chunks: [3, 3, 3] (10 records, Take(3) chunks = 9 records consumed)
        await Assert.That(chunks.Length).IsEqualTo(3);
        await Assert.That(chunks[0].Length).IsEqualTo(3);
        await Assert.That(chunks[1].Length).IsEqualTo(3);
        await Assert.That(chunks[2].Length).IsEqualTo(3);
    }

    [Test]
    public async ValueTask take_until_stops_on_predicate(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — take records until revision >= 5
        var records = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .TakeUntil(r => r.Position.Revision >= Revision.From(5))
            .ToArrayAsync(ct);

        // Assert — revisions 0..5 inclusive (TakeUntil includes the triggering element)
        await Assert.That(records.Length).IsEqualTo(6);
        await Assert.That(records.Last().Position.Revision).IsEqualTo(Revision.From(5));
    }

    [Test]
    public async ValueTask chunk_until_predicate_splits_on_condition(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — chunk records, breaking on every 4th record (revision % 4 == 3)
        var chunks = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .ChunkUntil(r => r.Position.Revision.Value % 4 == 3)
            .Take(2)
            .ToArrayAsync(ct);

        // Assert — 2 chunks of 4: [rev 0-3], [rev 4-7]
        await Assert.That(chunks.Length).IsEqualTo(2);
        await Assert.That(chunks[0].Length).IsEqualTo(4);
        await Assert.That(chunks[1].Length).IsEqualTo(4);
    }

    [Test]
    public async ValueTask scan_builds_running_state(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — build a running set of seen stream revisions
        var snapshots = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .Scan(new List<long>(), (revisions, record) => {
                revisions.Add(record.Position.Revision.Value);
                return revisions;
            })
            .Take(data.Records.Length)
            .LastAsync(ct);

        // Assert — final snapshot has all revisions
        await Assert.That(snapshots.Count).IsEqualTo(data.Records.Length);
        await Assert.That(snapshots).Contains(0L);
        await Assert.That(snapshots).Contains(data.Records.Length - 1);
    }

    [Test]
    public async ValueTask distinct_until_changed_deduplicates_consecutive(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(10, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — all records have the same stream, so DistinctUntilChanged on stream name yields 1
        var distinctStreams = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord.Position.Stream)
            .ToObservable()
            .DistinctUntilChanged()
            .Take(1)
            .ToArrayAsync(ct);

        // Assert
        await Assert.That(distinctStreams.Length).IsEqualTo(1);
        await Assert.That(distinctStreams[0]).IsEqualTo(data.Stream);
    }

    [Test]
    public async ValueTask select_many_flattens_chunks(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(9, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — chunk into groups of 3, then flatten back
        var records = await Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .Chunk(3)
            .Take(3)
            .SelectMany(chunk => chunk.ToObservable())
            .ToArrayAsync(ct);

        // Assert — same 9 records, chunked and flattened
        await Assert.That(records.Length).IsEqualTo(9);
    }

    [Test]
    public async ValueTask back_to_async_enumerable_roundtrips(CancellationToken ct) {
        // Arrange
        var data = await Fixture.SeedTestStream(5, ct);

        var options = new SubscribeOptions {
            Filter       = RecordFilter.ByStream(data.Stream),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        // Act — IAsyncEnumerable → Observable → IAsyncEnumerable
        var records = new List<Record>();

        await foreach (var record in Fixture.Client.Streams.Subscribe(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToObservable()
            .Scan(0, (count, r) => count + 1)
            .TakeUntil(count => count >= data.Records.Length)
            .Select(count => count)
            .ToAsyncEnumerable(ct)
        ) {
            records.Add(default); // just counting
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(data.Records.Length);
    }
}
