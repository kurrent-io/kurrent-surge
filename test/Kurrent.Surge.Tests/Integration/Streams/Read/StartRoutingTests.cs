using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Toolkit;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Tests.Streams;

/// <summary>
/// Start positions must be honored when filters re-route reads and subscriptions across
/// the position/revision boundary. Uses the interleaved layout so the anchor record
/// is foreign and INSIDE the target stream's range:
/// <code>
///   log order:  A0 A1 A2 | B0..B4 | A3 A4
/// </code>
/// </summary>
[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class StartRoutingTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask reads_log_filtered_by_stream_honors_start_position(CancellationToken ct) {
        // Arrange — the routed stream read must start at the first A-record at-or-after the
        // foreign anchor (B2), not at the beginning of the stream.
        var seed = await SeedInterleaved(ct);

        var anchor        = seed.B[2].Position.LogPosition;
        var expectedFirst = seed.A[3];
        var expectedCount = 2; // A3 + A4

        // Act — equality filter routes to a direct stream read
        Fixture.Logger.LogInformation(
            "Reading log from foreign anchor @{Anchor} filtered to {Stream} — expect [{First} ..], count {Count}",
            anchor.Value, seed.Stream, Describe(expectedFirst), expectedCount);

        var records = await Collect(new ReadRecordsOptions {
            Start        = anchor,
            Filter       = RecordFilter.ByExpression($"stream = '{seed.Stream}'"),
            SkipDecoding = true
        }, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    [Test]
    public async ValueTask read_records_with_default_start_and_stream_filter_reads_from_the_beginning(CancellationToken ct) {
        // Arrange — no Start given + Forwards (default) reads the stream from the BEGINNING. (It
        // used to default to the end and return nothing — that was the bug.)
        var seed          = await SeedInterleaved(ct);
        var expectedCount = seed.A.Count;

        // Act — default Start = Unset → resolved to Revision.Min for a forwards read
        var records = await Collect(new ReadRecordsOptions {
            Filter       = RecordFilter.ByExpression($"stream = '{seed.Stream}'"),
            SkipDecoding = true
        }, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
    }

    [Test]
    public async ValueTask read_records_with_native_stream_position_starts_at_matching_revision(CancellationToken ct) {
        // Arrange — a start whose stream half matches the target is used natively (inclusive)
        var seed = await SeedInterleaved(ct);

        var start         = seed.A[2].Position; // full record position of the target stream
        var expectedCount = 3;                  // A2 + A3 + A4

        // Act
        var records = await Collect(new ReadRecordsOptions {
            Start        = start,
            Filter       = RecordFilter.ByExpression($"stream = '{seed.Stream}'"),
            SkipDecoding = true
        }, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(seed.A[2].Id);
    }

    [Test]
    public async ValueTask read_stream_with_log_filter_honors_start_revision(CancellationToken ct) {
        // Arrange — the mirror bug: a non-stream filter re-routes ReadStream through the log;
        // the revision used to be silently discarded into "beginning of the log".
        var seed = await SeedInterleaved(ct);

        var startRevision = seed.A[3].Position.Revision;
        var expectedCount = 2; // A3 + A4

        // Act — LIKE on the full (unique) stream name is a log-level regex filter
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
            Start        = startRevision,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            SkipDecoding = true
        }, ct)) {
            if (msg.Value is Record record && record.Stream == seed.Stream)
                records.Add(record);
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(seed.A[3].Id);
    }

    [Test]
    public async ValueTask stream_subscribe_ignores_foreign_stream_half_and_resolves_log_half(CancellationToken ct) {
        // Arrange — a $all-style checkpoint: full record position of a FOREIGN record (B2).
        // Its revision half must never be applied to the target stream; the log half resolves
        // to the last processed A-record (A2), and the exclusive resume delivers A3 onwards.
        var seed = await SeedInterleaved(ct);

        var checkpoint    = seed.B[2].Position; // stream half = B, revision 2 — a trap
        var expectedFirst = seed.A[3];
        var expectedCount = 2; // A3 + A4

        // Act
        Fixture.Logger.LogInformation(
            "Subscribing to {Stream} from foreign checkpoint {Checkpoint} — expect [{First} ..], count {Count}",
            seed.Stream, checkpoint, Describe(expectedFirst), expectedCount);

        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(TimeSpan.FromSeconds(10));

        var records = new List<Record>();

        try {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                Start        = checkpoint,
                Filter       = RecordFilter.ByExpression($"stream = '{seed.Stream}'"),
                SkipDecoding = true
            }, cancellator.Token)) {
                if (msg.Value is Record record)
                    records.Add(record);

                if (msg.Value is CaughtUpCheckpoint)
                    break;
            }
        }
        catch (OperationCanceledException) {
            // safety net — assertions below still tell the truth
        }

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    [Test]
    public async ValueTask log_targeted_read_resolves_stream_revision_start_to_log_position(CancellationToken ct) {
        // Arrange — a stream-revision start on a log read is resolved (stream→log), not rejected:
        // ReadRecords and ReadStream converge on the same translation. Revision 2 of A resolves to
        // A2's log position; the filtered log read then delivers A2, A3, A4 (inclusive forwards).
        var seed = await SeedInterleaved(ct);

        var streamRevisionStart = RecordPosition.From(LogPosition.Unset, seed.Stream, seed.A[2].Position.Revision);
        var expectedFirst       = seed.A[2];
        var expectedCount       = 3; // A2 + A3 + A4

        // Act — LIKE on the full (unique) stream name is a log-level filter (log target)
        var records = await Collect(new ReadRecordsOptions {
            Start        = streamRevisionStart,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            SkipDecoding = true
        }, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_without_a_position_includes_last_stream_record(CancellationToken ct) {
        // Arrange — no start backwards anchors on the stream's last record (A4); a backwards $all
        // read excludes its start position, so the anchor must still arrive first.
        var seed = await SeedInterleaved(ct);

        var expectedIds = Enumerable.Reverse(seed.A).Select(r => r.Id).ToList(); // A4 .. A0

        // Act — LIKE on the full (unique) stream name is a log-level regex filter (log target)
        var records = await CollectBackTo(seed.A[0], Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            SkipDecoding = true
        }, ct));

        var actualIds = records.Select(r => r.Id).ToList();

        // Assert
        await Assert.That(actualIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_from_a_stream_revision_includes_the_anchor(CancellationToken ct) {
        // Arrange
        var seed = await SeedInterleaved(ct);

        var startRevision = seed.A[2].Position.Revision;
        var expectedIds   = new[] { seed.A[2].Id, seed.A[1].Id, seed.A[0].Id };

        // Act
        var records = await CollectBackTo(seed.A[0], Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
            Start        = startRevision,
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            SkipDecoding = true
        }, ct));

        var actualIds = records.Select(r => r.Id).ToList();

        // Assert
        await Assert.That(actualIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_from_revision_min_returns_the_first_stream_record(CancellationToken ct) {
        // Arrange
        var seed = await SeedInterleaved(ct);

        var expectedIds = new[] { seed.A[0].Id };

        // Act
        var records = await CollectBackTo(seed.A[0], Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
            Start        = Revision.Min,
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            SkipDecoding = true
        }, ct));

        var actualIds = records.Select(r => r.Id).ToList();

        // Assert
        await Assert.That(actualIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_from_a_stream_start_with_the_default_filter_includes_the_anchor(CancellationToken ct) {
        // Arrange — the default filter reads the whole log: the anchor (A3), then every record
        // before it in the log (B4 .. B0), down to A2.
        var seed = await SeedInterleaved(ct);

        var start       = RecordPosition.From(LogPosition.Unset, seed.Stream, seed.A[3].Position.Revision);
        var seededIds   = seed.A.Concat(seed.B).Select(r => r.Id).ToHashSet();
        var expectedIds = new[] { seed.A[3] }.Concat(Enumerable.Reverse(seed.B)).Append(seed.A[2]).Select(r => r.Id).ToList();

        // Act
        var records = await CollectBackTo(seed.A[2], Fixture.Client.Streams.ReadRecords(new ReadRecordsOptions {
            Start        = start,
            Direction    = ReadDirection.Backwards,
            SkipDecoding = true
        }, ct));

        var ownIds = records.Select(r => r.Id).Where(seededIds.Contains).ToList();

        // Assert
        await Assert.That(ownIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_skips_an_anchor_its_filter_rejects(CancellationToken ct) {
        // Arrange — anchored on A3, filtered to B: the anchor is not a match and never delivered.
        var seed = await SeedInterleaved(ct);

        var foreignStream = seed.B[0].Stream;
        var start         = RecordPosition.From(LogPosition.Unset, seed.Stream, seed.A[3].Position.Revision);
        var expectedIds   = Enumerable.Reverse(seed.B).Select(r => r.Id).ToList(); // B4 .. B0

        // Act
        var records = await CollectBackTo(seed.B[0], Fixture.Client.Streams.ReadRecords(new ReadRecordsOptions {
            Start        = start,
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{foreignStream}%'"),
            SkipDecoding = true
        }, ct));

        var actualIds = records.Select(r => r.Id).ToList();

        // Assert
        await Assert.That(actualIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    [Timeout(60_000)]
    public async ValueTask log_targeted_read_backwards_on_the_index_route_delivers_the_anchor_once(CancellationToken ct) {
        // Arrange — the secondary-index route reads backwards INCLUSIVE of its start, so the
        // anchor arrives from the server and must not be delivered a second time.
        var seed = await SeedInterleaved(ct);

        var streamIds   = seed.A.Select(r => r.Id).ToHashSet();
        var expectedIds = Enumerable.Reverse(seed.A).Select(r => r.Id).ToList(); // A4 .. A0
        var filter      = RecordFilter.ByCategory(seed.Stream.Category);

        // Act + Assert — poll until the secondary index catches up (eventually consistent)
        await Assert.That(async () => {
            var records = await CollectBackTo(seed.A[0], Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
                Direction    = ReadDirection.Backwards,
                Filter       = filter,
                SkipDecoding = true
            }, ct));

            var ownIds = records.Select(r => r.Id).Where(streamIds.Contains).ToList();

            await Assert.That(ownIds).IsDeepEquivalentTo(expectedIds);
        }).EventuallyPasses(CatchUp(ct));
    }

    [Test]
    public async ValueTask log_targeted_read_backwards_with_limit_one_returns_only_the_anchor(CancellationToken ct) {
        // Arrange
        var seed = await SeedInterleaved(ct);

        var expectedIds = new[] { seed.A[^1].Id };

        // Act
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStream(seed.Stream, new ReadStreamOptions {
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
            Limit        = 1,
            SkipDecoding = true
        }, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        var actualIds = records.Select(r => r.Id).ToList();

        // Assert
        await Assert.That(actualIds).IsDeepEquivalentTo(expectedIds);
    }

    [Test]
    [Timeout(60_000)]
    public async ValueTask log_targeted_read_backwards_anchored_on_a_link_stream_delivers_the_link_row_once(CancellationToken ct) {
        // Arrange — a category unique to this test, so $ce-{category} holds only its links. The log
        // route never resolves links: the anchor is the raw $> row, and its target appears once, at
        // its own position.
        var category   = $"anchorlink{Guid.NewGuid():N}";
        var stream     = StreamName.From($"{category}-{Guid.NewGuid():N}");
        var linkStream = StreamName.From($"$ce-{category}");
        var generated  = await Fixture.TestData.GenerateSet(3, ct);

        await Fixture.Client.Streams.AppendStream(stream, generated.Records, ct);

        var seeded = await ReadAllStreamRecords(stream, ct);
        var target = seeded[^1];

        Record? lastLink = null;

        await Assert.That(async () => {
            lastLink = await Fixture.Client.Streams
                .ReadStream(linkStream, new ReadStreamOptions { Direction = ReadDirection.Backwards, Limit = 1, SkipDecoding = true }, ct)
                .Where(msg => msg.IsRecord).Select(msg => msg.AsRecord)
                .FirstOrDefaultAsync(ct);

            var linkedId = lastLink?.Id;

            await Assert.That(linkedId).IsEqualTo(target.Id);
        }).EventuallyPasses(CatchUp(ct).RetryOn<StreamNotFoundException>());

        var expectedFirstPosition = lastLink!.Link.Position.LogPosition;

        // Act
        var records = await CollectBackTo(target, Fixture.Client.Streams.ReadRecords(new ReadRecordsOptions {
            Start        = RecordPosition.From(LogPosition.Unset, linkStream, Revision.Unset),
            Direction    = ReadDirection.Backwards,
            Filter       = RecordFilter.None,
            SkipDecoding = true
        }, ct));

        var first         = records[0];
        var firstPosition = first.Position.LogPosition;
        var firstIsLink   = first.IsLink;
        var targetCount   = records.Count(r => r.Id == target.Id);

        // Assert
        await Assert.That(firstPosition).IsEqualTo(expectedFirstPosition);
        await Assert.That(firstIsLink).IsTrue();
        await Assert.That(targetCount).IsEqualTo(1);
    }

    #region ->> Harness <<-

    // Secondary indexes and the standard projections are eventually consistent.
    static EventuallyOptions CatchUp(CancellationToken ct) => new() {
        Timeout           = TimeSpan.FromSeconds(15),
        PollingInterval   = TimeSpan.FromMilliseconds(500),
        CancellationToken = ct
    };

    // A backwards read runs to the start of the log; stop once it reaches the boundary record.
    static async ValueTask<List<Record>> CollectBackTo(Record boundary, IAsyncEnumerable<ReadMessage> read) {
        var records = new List<Record>();

        await foreach (var msg in read) {
            if (msg.Value is not Record record)
                continue;

            records.Add(record);

            if (record.Position.LogPosition <= boundary.Position.LogPosition)
                break;
        }

        return records;
    }

    record InterleavedSeed(StreamName Stream, List<Record> A, List<Record> B);

    async ValueTask<InterleavedSeed> SeedInterleaved(CancellationToken ct) {
        var a = await Fixture.SeedTestStream(3, ct);
        var b = await Fixture.SeedTestStream(5, ct);

        var more = await Fixture.TestData.GenerateSet(2, ct);
        await Fixture.Client.Streams.AppendStream(a.Stream, more.Records, ct);

        var aRecords = await ReadAllStreamRecords(a.Stream, ct);
        var bRecords = await ReadAllStreamRecords(b.Stream, ct);

        Fixture.Logger.LogInformation(
            "Seeded interleaved: A = {Stream} [{A}], foreign B = [{B}]",
            a.Stream,
            string.Join(" ", aRecords.Select(Describe)),
            string.Join(" ", bRecords.Select(Describe)));

        return new(a.Stream, aRecords, bRecords);
    }

    async ValueTask<List<Record>> Collect(ReadRecordsOptions options, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return records;
    }

    async ValueTask<List<Record>> ReadAllStreamRecords(StreamName stream, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStream(stream, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return records;
    }

    static string Describe(Record record) =>
        $"r{record.Position.Revision.Value}@{record.Position.LogPosition.Value}";

    #endregion // Harness
}
