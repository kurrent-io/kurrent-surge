using Kurrent.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Tests.Streams;

/// <summary>
/// The read counterpart of <see cref="ClientFilteredCheckpointTests"/>: when the SDK evaluates a
/// filter client-side during a read it discards records the consumer never sees, so it must raise
/// its own scan checkpoints to carry progress across those gaps — behaving as a server-side
/// filtered read would if the V1 read enumerators emitted them. The filter declares whether it
/// runs client-side via <c>RequiresClientEvaluation</c>; a filter the server fully satisfies adds
/// nothing.
/// </summary>
/// <remarks>
/// Reads differ from subscriptions on resume: a read start is INCLUSIVE (forwards), a subscription
/// resume is EXCLUSIVE (after). So a synthetic read checkpoint must watermark the FIRST record not
/// yet delivered, not the last one delivered — otherwise an inclusive resume re-includes (and
/// duplicates) the record at the checkpoint position. The resume test pins that off-by-one.
/// </remarks>
[Category("Streams")]
[Category("Read")]
[Category("Filters")]
[Category("Integration")]
[Timeout(30_000)]
public class ClientFilteredReadCheckpointTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Matches the target stream but is not server-expressible: a stream-equality disjunct unioned
    // with a non-stream disjunct poisons server extraction, so the whole filter runs client-side
    // over an unfiltered $all read. The schema disjunct names a value no record carries — it never
    // widens the match, it only forces the expression off the wire (RequiresClientEvaluation).
    static RecordFilter ClientSideFilter(StreamName target) =>
        RecordFilter.ByExpression($"stream = '{target}' OR schema_name = 'no-such-schema'");

    [Test]
    public async ValueTask client_filtered_read_emits_synthetic_checkpoints_through_barren_stretches(CancellationToken ct) {
        // Arrange — a long barren stretch of foreign records sits between the anchor and the target
        // stream. With window = 1 the scan must report progress before any match flows. Anchor at
        // the live end so the read only scans records seeded after it (bounded on a shared log).
        var anchor = await Fixture.LatestRecordPosition(ct);
        await Fixture.SeedTestStream(10, ct); // barren foreign records before the target
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;

        var records     = new List<Record>();
        var checkpoints = new List<FilterCheckpoint>();

        // Act — link resolution ON (the default): resolved links map the record position to an
        // earlier target event, so the watermark must track the $all scan position to stay
        // monotonic. This is the path that exposes a record-position watermark regressing.
        var options = new ReadRecordsOptions {
            Start                 = anchor.LogPosition,
            Filter                = ClientSideFilter(target.Stream),
            FilterMaxSearchWindow = 1,
            SkipDecoding          = true,
            ResolveLinks          = true
        };

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, cts.Token).ConfigureAwait(false)) {
                switch (msg.Value) {
                    case Record record:
                        records.Add(record);
                        if (records.Count >= expectedCount)
                            await cts.CancelAsync();
                        break;

                    case FilterCheckpoint fc:
                        checkpoints.Add(fc);
                        break;
                }
            }
        }

        Fixture.Logger.LogInformation(
            "Read {Records} records; {Checkpoints} synthetic checkpoints",
            records.Count, checkpoints.Count);

        // Assert — the target's records arrive, and progress was reported across the barren stretch:
        // at least one synthetic checkpoint landed strictly before the FIRST delivered record, which
        // can only happen while the scan walked foreign records the client discarded.
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(target.Stream);

        var firstMatchPosition = records[0].Position.LogPosition.Value;

        await Assert.That(checkpoints).IsNotEmpty();
        await Assert.That(checkpoints.Any(cp => cp.Positions[0].LogPosition.Value < firstMatchPosition)).IsTrue();
    }

    [Test]
    public async ValueTask read_synthetic_checkpoint_positions_are_monotonic(CancellationToken ct) {
        // Arrange — `stream = 'X' AND <predicate>` routes to a single-stream read (no server filter
        // checkpoints at all), and the predicate `log_position < 0` is never true, so every scanned
        // record is discarded client-side. With window = 5 over 20 records the only progress signal
        // is the SDK's synthesized checkpoints — pure invisible progress, ideal to pin monotonicity.
        var target = await Fixture.SeedTestStream(20, ct);

        const int window = 5;
        var       filter = RecordFilter.ByExpression($"stream = '{target.Stream}' AND log_position < 0");

        var checkpoints      = new List<FilterCheckpoint>();
        var deliveredRecords = 0;

        // Act — a single-stream read terminates at end-of-stream (CaughtUp), so it is naturally
        // bounded; collect every synthetic checkpoint produced over the 20 scanned records.
        var options = new ReadRecordsOptions {
            Start                 = LogPosition.Earliest,
            Filter                = filter,
            FilterMaxSearchWindow = window,
            SkipDecoding          = true,
            ResolveLinks          = false
        };

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct).ConfigureAwait(false)) {
            switch (msg.Value) {
                case Record:              deliveredRecords++;  break;
                case FilterCheckpoint fc: checkpoints.Add(fc); break;
            }
        }

        // Assert — everything discarded, yet the watermark advanced via synthesized checkpoints at
        // roughly one per window over the 20 scanned records, strictly non-regressing.
        var perWindow = target.Records.Length / (int)window;

        await Assert.That(deliveredRecords).IsEqualTo(0);
        await Assert.That(checkpoints).IsNotEmpty();
        await Assert.That(checkpoints.Count)
            .IsGreaterThanOrEqualTo(perWindow - 1).And
            .IsLessThanOrEqualTo(perWindow + 1);

        for (var i = 1; i < checkpoints.Count; i++)
            await Assert.That(checkpoints[i].Positions[0].LogPosition.Value)
                .IsGreaterThanOrEqualTo(checkpoints[i - 1].Positions[0].LogPosition.Value);
    }

    [Test]
    public async ValueTask resuming_a_read_from_a_synthetic_checkpoint_loses_nothing_and_duplicates_nothing(CancellationToken ct) {
        // Arrange — THE off-by-one test. A read start is INCLUSIVE: resuming from a checkpoint
        // position re-reads the record AT that position. The synthetic checkpoint must therefore
        // point at the first NOT-yet-delivered record, so an inclusive resume re-includes only
        // un-consumed matches. Raw log (ResolveLinks = false): each target record is delivered
        // exactly once at its own position, so duplication is observable as a revision reappearing.
        var anchor = await Fixture.LatestRecordPosition(ct);
        await Fixture.SeedTestStream(8, ct); // barren foreign records before the target
        var target = await Fixture.SeedTestStream(5, ct);

        var filter = ClientSideFilter(target.Stream);

        const int consumeBeforeResume = 2;
        var       expectedAfter       = target.Records.Length - consumeBeforeResume;

        var deliveredBefore = new List<Revision>();
        var resumeFrom      = RecordPosition.Unset;

        // Act 1 — consume until two target records are delivered, then take the NEXT synthetic
        // checkpoint as the resume cursor and STOP. Stopping on capture is the contract a real
        // consumer honors: persisting cp@N means "N not yet processed", so it must not consume past
        // the checkpoint it persists. With window = 1 a checkpoint precedes every scanned record,
        // so the cursor sits strictly after the second delivered record and at-or-before the third.
        await foreach (var msg in Fixture.Client.Streams.ReadRecords(new ReadRecordsOptions {
                           Start                 = anchor.LogPosition,
                           Filter                = filter,
                           FilterMaxSearchWindow = 1,
                           SkipDecoding          = true,
                           ResolveLinks          = false
                       }, ct).ConfigureAwait(false)) {
            if (msg.Value is Record record) {
                deliveredBefore.Add(record.Revision);
                continue;
            }

            if (msg.Value is FilterCheckpoint checkpoint && deliveredBefore.Count >= consumeBeforeResume) {
                resumeFrom = checkpoint.Positions[0];
                break;
            }
        }

        // Act 2 — resume the read from the captured cursor (inclusive forwards) and collect the rest.
        var deliveredAfter = new List<Revision>();

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(new ReadRecordsOptions {
                           Start                 = resumeFrom,
                           Filter                = filter,
                           FilterMaxSearchWindow = 1,
                           SkipDecoding          = true,
                           ResolveLinks          = false
                       }, ct).ConfigureAwait(false)) {
            if (msg.Value is not Record record)
                continue;

            deliveredAfter.Add(record.Revision);

            if (deliveredAfter.Count >= expectedAfter)
                break;
        }

        // Assert — resume lost nothing and duplicated nothing: the two halves are disjoint (no
        // re-delivered match) and together cover every target revision exactly once.
        var allTargetRevisions = deliveredBefore.Concat(deliveredAfter).ToList();
        var expectedRevisions  = Enumerable.Range(0, target.Records.Length).Select(i => Revision.From(i)).ToList();

        await Assert.That(resumeFrom.IsUnset).IsFalse();
        await Assert.That(deliveredBefore.Count).IsEqualTo(consumeBeforeResume);
        await Assert.That(deliveredAfter.Count).IsEqualTo(expectedAfter);
        await Assert.That(deliveredBefore.Intersect(deliveredAfter)).IsEmpty();
        await Assert.That(allTargetRevisions.OrderBy(r => r.Value)).IsEquivalentTo(expectedRevisions);
    }

    [Test]
    public async ValueTask pure_server_filter_read_synthesizes_nothing(CancellationToken ct) {
        // Arrange — ByStream is a pure server-routable filter with no client refinement: every
        // delivered record matches, so the SDK discards nothing and must synthesize nothing. With
        // window = 1 a stray synthesis would emit one checkpoint per record, so an empty result is
        // a strong guard.
        var target        = await Fixture.SeedTestStream(5, ct);
        var expectedCount = target.Records.Length;

        var records     = new List<Record>();
        var checkpoints = new List<FilterCheckpoint>();

        // Act
        var options = new ReadRecordsOptions {
            Start                 = LogPosition.Earliest,
            Filter                = RecordFilter.ByStream(target.Stream),
            FilterMaxSearchWindow = 1,
            Limit                 = expectedCount,
            SkipDecoding          = true
        };

        await foreach (var msg in Fixture.Client.Streams.ReadRecords(options, ct).ConfigureAwait(false)) {
            switch (msg.Value) {
                case Record record:       records.Add(record);     break;
                case FilterCheckpoint fc: checkpoints.Add(fc);      break;
            }
        }

        // Assert — records arrive; zero synthetic checkpoints.
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(checkpoints).IsEmpty();
    }
}
