using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// DEV-1772 — start positions must be honored when filters re-route reads and subscriptions
/// across the position/revision boundary. Uses the interleaved layout so the anchor record
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
    public async ValueTask read_records_with_default_start_and_stream_filter_returns_no_history(CancellationToken ct) {
        // Arrange — THE headline bug: default Start (end of log) + stream filter used to
        // silently read the ENTIRE stream from the beginning.
        var seed = await SeedInterleaved(ct);

        // Act — default Start = Latest; nothing historical may flow
        var records = await Collect(new ReadRecordsOptions {
            Filter       = RecordFilter.ByExpression($"stream = '{seed.Stream}'"),
            SkipDecoding = true
        }, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(0);
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
    public async ValueTask log_targeted_read_rejects_start_without_log_position(CancellationToken ct) {
        // Arrange — a stream-only start cannot address the log; guessing is forbidden
        var seed = await SeedInterleaved(ct);

        var streamOnly = RecordPosition.From(LogPosition.Unset, seed.Stream, seed.A[2].Position.Revision);

        // Act & Assert — loud, not silent
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await Collect(new ReadRecordsOptions {
                Start        = streamOnly,
                Filter       = RecordFilter.ByExpression($"stream LIKE '{seed.Stream}%'"),
                SkipDecoding = true
            }, ct));
    }

    #region ->> Harness <<-

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

        await foreach (var msg in Fixture.Client.Streams.ReadStreamRecords(stream, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return records;
    }

    static string Describe(Record record) =>
        $"r{record.Position.Revision.Value}@{record.Position.LogPosition.Value}";

    #endregion // Harness
}
