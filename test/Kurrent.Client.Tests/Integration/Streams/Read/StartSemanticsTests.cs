using Grpc.Core;
using Kurrent.Client.Streams;
using KurrentDB.Protocol.V1.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// Pins the server's Start position semantics so any drift breaks loudly on image bumps.
/// Probe-verified ground truth:
/// <code>
///   Path                  Semantics
///   ───────────────────── ─────────────────────────────
///   $all read forwards    INCLUSIVE (record AT position)
///   $all read backwards   EXCLUSIVE (server quirk)
///   stream read fwd/back  INCLUSIVE
///   subscriptions         EXCLUSIVE ("after", server skips &lt;= checkpoint)
/// </code>
/// The SDK maps Start verbatim to the wire — these tests are the executable contract
/// until an explicit AT/AFTER start API makes the choice visible per call site.
/// Re-subscribe tests pin the continuity contract that cursor-resume logic relies on.
/// </summary>
[Category("Streams")]
[Category("Read")]
[Category("Integration")]
[Timeout(30_000)]
public class StartSemanticsTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    #region ->> $all Reads <<-

    [Test]
    public async ValueTask all_read_forwards_includes_record_at_start_position(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, filter) = await SeedWithPivot(ct);

        var expectedFirstId = pivot.Id;
        var expectedCount   = 3; // pivot (r2) + r3 + r4

        // Act
        Fixture.Logger.LogInformation(
            "Reading $all FORWARDS from pivot {Pivot} — expect INCLUSIVE: first = pivot, count = {Count}",
            Describe(pivot), expectedCount);

        var records = await CollectRead(new ReadRecordsOptions {
            Start = pivot.Position.LogPosition, Filter = filter, SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async ValueTask all_read_backwards_excludes_record_at_start_position(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, filter) = await SeedWithPivot(ct);

        var expectedFirst = seeded[1]; // record BEFORE the pivot — server quirk: backwards $all is exclusive
        var expectedCount = 2;         // r1 + r0

        // Act
        Fixture.Logger.LogInformation(
            "Reading $all BACKWARDS from pivot {Pivot} — expect EXCLUSIVE (server quirk): first = {First}, count = {Count}",
            Describe(pivot), Describe(expectedFirst), expectedCount);

        var records = await CollectRead(new ReadRecordsOptions {
            Start = pivot.Position.LogPosition, Filter = filter, Direction = ReadDirection.Backwards, SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    #endregion // $all Reads

    #region ->> Stream Reads <<-

    [Test]
    public async ValueTask stream_read_forwards_includes_record_at_start_revision(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, _) = await SeedWithPivot(ct);

        var expectedFirstId = pivot.Id;
        var expectedCount   = 3; // r2 + r3 + r4

        // Act
        Fixture.Logger.LogInformation(
            "Reading stream FORWARDS from revision {Revision} — expect INCLUSIVE: first = pivot {Pivot}, count = {Count}",
            pivot.Position.Revision.Value, Describe(pivot), expectedCount);

        var records = await CollectStreamRead(pivot.Position.Stream, new ReadStreamOptions {
            Start = pivot.Position.Revision, SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async ValueTask stream_read_backwards_includes_record_at_start_revision(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, _) = await SeedWithPivot(ct);

        var expectedFirstId = pivot.Id; // unlike $all, stream backwards is inclusive
        var expectedCount   = 3;        // r2 + r1 + r0

        // Act
        Fixture.Logger.LogInformation(
            "Reading stream BACKWARDS from revision {Revision} — expect INCLUSIVE: first = pivot {Pivot}, count = {Count}",
            pivot.Position.Revision.Value, Describe(pivot), expectedCount);

        var records = await CollectStreamRead(pivot.Position.Stream, new ReadStreamOptions {
            Start = pivot.Position.Revision, Direction = ReadDirection.Backwards, SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirstId);
    }

    #endregion // Stream Reads

    #region ->> $all Subscriptions <<-

    [Test]
    public async ValueTask all_subscribe_excludes_record_at_start_position(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, filter) = await SeedWithPivot(ct);

        var expectedFirst = seeded[3]; // strictly AFTER the pivot
        var expectedCount = 2;         // r3 + r4

        // Act
        Fixture.Logger.LogInformation(
            "Subscribing to $all from pivot {Pivot} — expect EXCLUSIVE: first = {First}, count = {Count}",
            Describe(pivot), Describe(expectedFirst), expectedCount);

        var records = await CollectSubscribe(new SubscribeOptions {
            Start = pivot.Position, Filter = filter, SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    [Test]
    public async ValueTask all_resubscribe_from_last_record_position_delivers_only_new_records(CancellationToken ct) =>
        await VerifyResubscribeContinuity(viaAllLog: true, "last record position", (own, _) => own[^1].Position, ct);

    [Test]
    public async ValueTask all_resubscribe_from_caught_up_checkpoint_delivers_only_new_records(CancellationToken ct) =>
        await VerifyResubscribeContinuity(viaAllLog: true, "CaughtUp checkpoint", (_, caughtUp) => caughtUp.Positions[0], ct);

    #endregion // $all Subscriptions

    #region ->> Stream Subscriptions <<-

    [Test]
    public async ValueTask stream_subscribe_excludes_record_at_start_revision(CancellationToken ct) {
        // Arrange
        var (seeded, pivot, _) = await SeedWithPivot(ct);

        var expectedFirst = seeded[3]; // strictly AFTER the pivot
        var expectedCount = 2;         // r3 + r4

        // Act — equality filter routes to a stream subscription; start revision comes from pivot.Position
        Fixture.Logger.LogInformation(
            "Subscribing to stream from revision {Revision} — expect EXCLUSIVE: first = {First}, count = {Count}",
            pivot.Position.Revision.Value, Describe(expectedFirst), expectedCount);

        var records = await CollectSubscribe(new SubscribeOptions {
            Start        = pivot.Position,
            Filter       = RecordFilter.ByExpression($"stream = '{pivot.Position.Stream}'"),
            SkipDecoding = true
        }, seeded, ct);

        // Assert
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(records[0].Id).IsEqualTo(expectedFirst.Id);
    }

    [Test]
    public async ValueTask stream_resubscribe_from_last_record_position_delivers_only_new_records(CancellationToken ct) =>
        await VerifyResubscribeContinuity(viaAllLog: false, "last record position", (own, _) => own[^1].Position, ct);

    [Test]
    public async ValueTask stream_resubscribe_from_caught_up_checkpoint_delivers_only_new_records(CancellationToken ct) =>
        await VerifyResubscribeContinuity(viaAllLog: false, "CaughtUp checkpoint", (_, caughtUp) => caughtUp.Positions[0], ct);

    /// <summary>
    /// Pins what the V1 wire CaughtUp carries for a stream subscription. The resilient
    /// subscription resumes from this value (Subscribe.Resilient.cs sets Cursor from the
    /// CaughtUp checkpoint) and subscriptions are EXCLUSIVE — so if the server reported the
    /// NEXT-EXPECTED revision instead of the LAST-WRITTEN one, a reconnect after CaughtUp
    /// would skip the next record (data loss). This test asserts the safe contract.
    /// </summary>
    [Test]
    public async ValueTask stream_subscribe_caught_up_carries_last_written_revision(CancellationToken ct) {
        // Arrange
        var simulation = await Fixture.SeedTestStream(5, ct);
        var stream     = simulation.Stream;

        var expectedRevision = simulation.Revision.Value; // last written = 4

        // Raw V1 subscription — the public Subscribe API resolves the CaughtUp watermark by
        // re-reading the stream, masking the wire value; the resilient cursor uses the raw one.
        var request = StreamsClientMapping.Requests.CreateSubscriptionRequest(
            StreamNameFilter.Parse(stream),
            RecordPosition.From(LogPosition.Earliest, stream, Revision.Min),
            resolveLinks: false);

        // Act
        Fixture.Logger.LogInformation(
            "Raw V1 stream subscription on {Stream} — expect CaughtUp to carry LAST-WRITTEN revision {Expected}",
            stream, expectedRevision);

        long? caughtUpRevision = null;

        using var session = Fixture.Client.Streams.LegacyServiceClient.Read(request, cancellationToken: ct);

        while (await session.ResponseStream.MoveNext(ct)) {
            var rsp = session.ResponseStream.Current;

            if (rsp.ContentCase != ReadResp.ContentOneofCase.CaughtUp)
                continue;

            caughtUpRevision = rsp.CaughtUp.HasStreamRevision ? rsp.CaughtUp.StreamRevision : null;
            break;
        }

        Fixture.Logger.LogInformation(
            "CaughtUp wire revision: {Actual} (last written: {Expected})",
            caughtUpRevision, expectedRevision);

        // Assert
        await Assert.That(caughtUpRevision).IsNotNull();
        await Assert.That(caughtUpRevision!.Value).IsEqualTo(expectedRevision);
    }

    #endregion // Stream Subscriptions

    #region ->> Harness <<-

    /// <summary>
    /// Seeds 5 records and returns them with the middle record as pivot, so both
    /// directions have neighbours, plus a LIKE filter on the full (unique) stream name —
    /// LIKE keeps requests on the $all branch (StreamRegexFilter), where an equality
    /// filter would reroute to a stream read.
    /// </summary>
    async ValueTask<(List<Record> Seeded, Record Pivot, RecordFilter AllFilter)> SeedWithPivot(CancellationToken ct) {
        var simulation = await Fixture.SeedTestStream(5, ct);

        var seeded = await ReadAllStreamRecords(simulation.Stream, ct);

        await Assert.That(seeded.Count).IsEqualTo(5);

        var pivot = seeded[2];

        Fixture.Logger.LogInformation(
            "Seeded {Stream}: [{Records}] — pivot = {Pivot}",
            simulation.Stream, string.Join(" ", seeded.Select(Describe)), Describe(pivot));

        return (seeded, pivot, RecordFilter.ByExpression($"stream LIKE '{simulation.Stream}%'"));
    }

    /// <summary>
    /// The re-subscribe contract — what the resilient subscription does internally on every
    /// reconnect (Subscribe.Resilient.cs cursor resume): drop a live subscription, resume from
    /// the cursor (last record or CaughtUp checkpoint), and the consumer sees a seamless
    /// stream — every record appended after the cursor arrives (no loss), nothing already
    /// delivered repeats (no duplication).
    /// </summary>
    async ValueTask VerifyResubscribeContinuity(
        bool viaAllLog,
        string cursorKind,
        Func<List<Record>, CaughtUpCheckpoint, RecordPosition> selectCursor,
        CancellationToken ct
    ) {
        var target = viaAllLog ? "$all" : "stream";

        // Arrange — phase 1: seed, subscribe, drain to CaughtUp, capture the cursor.
        // $all subscribes from the pre-seed log tail (a full-log scan from Earliest would
        // crawl the shared container's history); stream subscribes from Earliest — a foreign
        // record's revision must never be used as a stream cursor.
        var phase1Start = viaAllLog ? await Fixture.LatestRecordPosition(ct) : RecordPosition.Earliest;

        var simulation = await Fixture.SeedTestStream(5, ct);
        var stream     = simulation.Stream;

        // LIKE keeps the $all branch (StreamRegexFilter); equality routes to the stream branch.
        var filter = viaAllLog
            ? RecordFilter.ByExpression($"stream LIKE '{stream}%'")
            : RecordFilter.ByExpression($"stream = '{stream}'");

        var oldSeeded = await ReadAllStreamRecords(stream, ct);

        Fixture.Logger.LogInformation(
            "Phase 1: seeded {Stream}: [{Records}]; subscribing to {Target} — expect all 5, then CaughtUp",
            stream, string.Join(" ", oldSeeded.Select(Describe)), target);

        var (phase1, caughtUp) = await CollectSubscribeSession(new SubscribeOptions {
            Start = phase1Start, Filter = filter, SkipDecoding = true
        }, oldSeeded, ct);

        await Assert.That(phase1.Count).IsEqualTo(5);
        await Assert.That(caughtUp).IsNotNull();

        var cursor = selectCursor(phase1, caughtUp!);

        Fixture.Logger.LogInformation(
            "Cursor captured ({CursorKind}): r{Revision}@{Position} — dropping subscription",
            cursorKind, cursor.Revision.Value, cursor.LogPosition.Value);

        // Append 5 more records to the SAME stream — these land after the cursor.
        var more = await Fixture.TestData.GenerateSet(5, ct);
        await Fixture.Client.Streams.AppendStream(stream, more.Records, ct);

        var allSeeded = await ReadAllStreamRecords(stream, ct);
        var newSeeded = allSeeded[5..];

        var expectedFirst = newSeeded[0];
        var expectedCount = newSeeded.Count;
        var oldIds        = oldSeeded.Select(r => r.Id).ToList();

        // Act — phase 2: re-subscribe from the cursor.
        Fixture.Logger.LogInformation(
            "Phase 2: appended [{Records}]; resuming {Target} from cursor — expect exactly the {Count} new records (no loss, no duplication), first = {First}",
            string.Join(" ", newSeeded.Select(Describe)), target, expectedCount, Describe(expectedFirst));

        var (resumed, _) = await CollectSubscribeSession(new SubscribeOptions {
            Start = cursor, Filter = filter, SkipDecoding = true
        }, allSeeded, ct);

        // Assert — no loss, no duplication.
        await Assert.That(resumed.Count).IsEqualTo(expectedCount);
        await Assert.That(resumed[0].Id).IsEqualTo(expectedFirst.Id);
        await Assert.That(resumed.Select(r => r.Id).Intersect(oldIds).ToList()).IsEmpty();
    }

    async ValueTask<List<Record>> CollectRead(ReadRecordsOptions options, List<Record> seeded, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return OwnRecords(records, seeded);
    }

    async ValueTask<List<Record>> CollectStreamRead(StreamName stream, ReadStreamOptions options, List<Record> seeded, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStream(stream, options, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return OwnRecords(records, seeded);
    }

    async ValueTask<List<Record>> CollectSubscribe(SubscribeOptions options, List<Record> seeded, CancellationToken ct) {
        var (records, _) = await CollectSubscribeSession(options, seeded, ct);
        return records;
    }

    async ValueTask<(List<Record> Records, CaughtUpCheckpoint? CaughtUp)> CollectSubscribeSession(
        SubscribeOptions options, List<Record> seeded, CancellationToken ct
    ) {
        // A live subscription never completes: the seeded records arrive during catch-up,
        // so the first CaughtUp checkpoint marks the end of the relevant window.
        using var cancellator = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cancellator.CancelAfter(TimeSpan.FromSeconds(10));

        var records = new List<Record>();

        CaughtUpCheckpoint? caughtUp = null;

        try {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cancellator.Token)) {
                if (msg.Value is Record record)
                    records.Add(record);

                if (msg.Value is CaughtUpCheckpoint checkpoint) {
                    caughtUp = checkpoint;
                    break;
                }
            }
        }
        catch (OperationCanceledException) {
            // safety net fired — own-record assertions below still tell the truth
        }

        return (OwnRecords(records, seeded), caughtUp);
    }

    async ValueTask<List<Record>> ReadAllStreamRecords(StreamName stream, CancellationToken ct) {
        var records = new List<Record>();

        await foreach (var msg in Fixture.Client.Streams.ReadStreamRecords(stream, ct)) {
            if (msg.Value is Record record)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Filters down to the test's own seeded records — the shared container has
    /// concurrent traffic from parallel tests — and logs the observed outcome.
    /// </summary>
    List<Record> OwnRecords(List<Record> returned, List<Record> seeded) {
        var seededIds = seeded.Select(r => r.Id).ToHashSet();
        var own       = returned.Where(r => seededIds.Contains(r.Id)).ToList();

        Fixture.Logger.LogInformation(
            "Observed {Own} own records of {Returned} returned: [{Records}]",
            own.Count, returned.Count, string.Join(" ", own.Select(Describe)));

        return own;
    }

    /// <summary>Compact record tag for the narrative log: r{revision}@{log position}.</summary>
    static string Describe(Record record) =>
        $"r{record.Position.Revision.Value}@{record.Position.LogPosition.Value}";

    #endregion // Harness
}
