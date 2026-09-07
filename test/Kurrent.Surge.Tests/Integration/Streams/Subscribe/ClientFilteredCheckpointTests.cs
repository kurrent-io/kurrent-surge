using Kurrent.Surge.Client.Streams;
using Microsoft.Extensions.Logging;

namespace Kurrent.Surge.Tests.Streams;

/// <summary>
/// Whenever the SDK evaluates a filter client-side it discards records the consumer never sees,
/// so it must raise its own scan checkpoints to carry progress across those gaps — behaving
/// exactly as a server-side filtered subscription would. This holds regardless of the target
/// (<c>$all</c> or single stream) and regardless of whether a server-side filter is also present;
/// the filter itself declares whether it runs client-side via <c>RequiresClientEvaluation</c>.
/// A filter the server fully satisfies adds nothing.
/// </summary>
[Category("Streams")]
[Category("Subscribe")]
[Category("Integration")]
[Timeout(30_000)]
public class ClientFilteredCheckpointTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    // Matches the target stream but is not server-expressible: a stream-equality disjunct unioned
    // with a non-stream disjunct poisons server extraction, so the whole filter runs client-side
    // over an unfiltered $all subscription. The schema disjunct names a value no record carries —
    // it never widens the match, it only forces the expression off the wire.
    static RecordFilter ClientSideFilter(StreamName target) =>
        RecordFilter.ByExpression($"stream = '{target}' OR schema_name = 'no-such-schema'");

    // Matches every record but still runs client-side (the poisoned union derives no server
    // filter). Lets a test observe the checkpoint-per-window cadence with nothing discarded.
    static RecordFilter MatchAllClientSide() =>
        RecordFilter.ByExpression("stream LIKE '%' OR schema_name = 'never-matches-anything'");

    [Test]
    public async ValueTask client_filtered_subscription_emits_synthetic_checkpoints_through_barren_stretches(CancellationToken ct) {
        // Arrange — a long barren stretch of foreign records sits between the anchor and the
        // target stream. With window = 1 the scan must report progress before any match flows.
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(10, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var expectedCount = target.Records.Length;

        var checkpoints = new List<Checkpoint>();

        // Act — link resolution ON (the default): resolved links map the record position to an
        // earlier target event, so the watermark must track the $all scan position to stay
        // monotonic. This is the path that exposes a record-position watermark regressing.
        var records = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = anchor,
                Filter          = ClientSideFilter(target.Stream),
                FilterMaxSearchWindow = 1,
                SkipDecoding    = true,
                ResolveLinks    = true
            }, ct)
            .Records(checkpoints.Add, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        Fixture.Logger.LogInformation(
            "Projected {Records} records; handler received {Checkpoints} synthetic checkpoints",
            records.Count, checkpoints.Count);

        // Assert — the target's records arrive, and progress was reported across the barren
        // stretch: at least one synthetic checkpoint landed strictly before the FIRST delivered
        // record, which can only happen while the scan walked foreign records the client discarded.
        await Assert.That(records.Count).IsEqualTo(expectedCount);

        foreach (var record in records)
            await Assert.That(record.Stream).IsEqualTo(target.Stream);

        var firstMatchPosition = records[0].Position.LogPosition.Value;
        var synthetic          = checkpoints.OfType<FilterCheckpoint>().ToList();

        await Assert.That(synthetic).IsNotEmpty();
        await Assert.That(synthetic.Any(cp => cp.Positions[0].LogPosition.Value < firstMatchPosition)).IsTrue();

        for (var i = 1; i < synthetic.Count; i++)
            await Assert.That(synthetic[i].Positions[0].LogPosition.Value)
                .IsGreaterThanOrEqualTo(synthetic[i - 1].Positions[0].LogPosition.Value);
    }

    [Test]
    public async ValueTask synthetic_checkpoint_positions_are_monotonic_and_resumable(CancellationToken ct) {
        // Arrange — barren records, then a target stream. We consume past two matches, capture the
        // next synthetic checkpoint, resume from it, and prove the remaining matches arrive once.
        var anchor = await Fixture.LatestRecordPosition(ct);
        var barren = await Fixture.SeedTestStream(8, ct);
        var target = await Fixture.SeedTestStream(5, ct);

        var filter = ClientSideFilter(target.Stream);

        const int consumeBeforeResume = 2;
        var       expectedAfter       = target.Records.Length - consumeBeforeResume;

        var watermarks      = new List<long>();
        var deliveredBefore = new List<Revision>();
        var resumeFrom      = RecordPosition.Unset;

        // Act 1 — consume until two target records are delivered, then take the next checkpoint as
        // the resume cursor. With window = 1 a checkpoint follows every scanned record, so the
        // checkpoint captured here sits at (or after) the second delivered record's position.
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start           = anchor,
                               Filter          = filter,
                               FilterMaxSearchWindow = 1,
                               SkipDecoding    = true,
                               // Raw log, no link resolution: each target record is delivered
                               // exactly once at its own position, so resume math is exact.
                               ResolveLinks    = false
                           }, cts.Token).ConfigureAwait(false)) {
                if (msg.Value is Record record) {
                    deliveredBefore.Add(record.Revision);
                    continue;
                }

                if (msg.Value is not Checkpoint checkpoint)
                    continue;

                watermarks.Add(checkpoint.Positions[0].LogPosition.Value);

                if (deliveredBefore.Count >= consumeBeforeResume) {
                    resumeFrom = checkpoint.Positions[0];
                    await cts.CancelAsync();
                }
            }
        }

        // Act 2 — resume from the captured checkpoint (exclusive) and collect the rest.
        var deliveredAfter = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = resumeFrom,
                Filter          = filter,
                FilterMaxSearchWindow = 1,
                SkipDecoding    = true,
                ResolveLinks    = false
            }, ct)
            .Records(_ => { }, ct)
            .Take(expectedAfter)
            .Select(r => r.Revision)
            .ToListAsync(ct);

        // Assert — watermarks never regress; resume lost nothing and duplicated nothing.
        await Assert.That(resumeFrom.IsUnset).IsFalse();

        for (var i = 1; i < watermarks.Count; i++)
            await Assert.That(watermarks[i]).IsGreaterThanOrEqualTo(watermarks[i - 1]);

        var allTargetRevisions = deliveredBefore.Concat(deliveredAfter).ToList();

        await Assert.That(deliveredAfter.Count).IsEqualTo(expectedAfter);
        await Assert.That(deliveredBefore.Intersect(deliveredAfter)).IsEmpty();
        await Assert.That(allTargetRevisions.Distinct().Count()).IsEqualTo(target.Records.Length);
    }

    [Test]
    public async ValueTask resume_from_synthetic_checkpoint_loses_nothing_under_link_resolution(CancellationToken ct) {
        // Arrange — resumability on the path that motivated the scan-position watermark: link
        // resolution ON, where the mapped record position and the scan position diverge.
        var anchor = await Fixture.LatestRecordPosition(ct);
        await Fixture.SeedTestStream(8, ct); // barren foreign records sit before the target
        var target = await Fixture.SeedTestStream(5, ct);

        var filter     = ClientSideFilter(target.Stream);
        var resumeFrom = RecordPosition.Unset;

        // Act 1 — capture the first synthetic checkpoint, produced while scanning the barren stretch
        // (before any target match), so the cursor is guaranteed to sit before every target record.
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start = anchor, Filter = filter, FilterMaxSearchWindow = 1,
                               SkipDecoding = true, ResolveLinks = true
                           }, cts.Token).ConfigureAwait(false)) {
                if (msg.Value is not Checkpoint checkpoint)
                    continue;

                resumeFrom = checkpoint.Positions[0];
                await cts.CancelAsync();
            }
        }

        // Act 2 — resume from that scan-position cursor; every target record must still arrive.
        // Link resolution can surface a target event more than once ($ce/$et links), so prove
        // no-loss as a set, tolerating duplicates.
        var deliveredRevisions = new HashSet<Revision>();

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start = resumeFrom, Filter = filter, FilterMaxSearchWindow = 1,
                               SkipDecoding = true, ResolveLinks = true
                           }, cts.Token).ConfigureAwait(false)) {
                if (msg.Value is not Record record)
                    continue;

                deliveredRevisions.Add(record.Revision);

                if (deliveredRevisions.Count == target.Records.Length)
                    await cts.CancelAsync();
            }
        }

        // Assert — resuming from the synthetic (scan-position) checkpoint lost no target record.
        var expectedRevisions = Enumerable.Range(0, target.Records.Length).Select(i => Revision.From(i)).ToHashSet();

        await Assert.That(resumeFrom.IsUnset).IsFalse();
        await Assert.That(deliveredRevisions).IsEquivalentTo(expectedRevisions);
    }

    [Test]
    public async ValueTask hybrid_filtered_subscription_emits_synthetic_checkpoints_when_refinement_discards(CancellationToken ct) {
        // Arrange — a hybrid filter on the $all-filtered path: the server-expressible part
        // (`stream LIKE 'target%'`) bounds the server's *delivery* to the target stream (load-robust
        // — it never streams the whole log), and a client refinement (`log_position < 0`, never
        // true) discards every delivered record. The server MATCHES those records, so it never
        // checkpoints on them; the only thing that can put a watermark on the discarded run is the
        // SDK synthesizing. Before the fix this was a progress black hole in the hybrid case.
        var anchor = await Fixture.LatestRecordPosition(ct);
        await Fixture.SeedTestStream(10, ct); // barren foreign records before the target
        var target = await Fixture.SeedTestStream(5, ct);

        var filter = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%' AND log_position < 0");

        var checkpoints      = new List<FilterCheckpoint>();
        var deliveredRecords = 0;

        // Act — scan until a checkpoint reaches the target head; by then the target run is scanned.
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start           = anchor,
                               Filter          = filter,
                               FilterMaxSearchWindow = 1,
                               SkipDecoding    = true,
                               ResolveLinks    = false
                           }, cts.Token).ConfigureAwait(false)) {
                switch (msg.Value) {
                    case Record: deliveredRecords++; break;

                    case FilterCheckpoint fc:
                        checkpoints.Add(fc);
                        if (fc.Positions[0].LogPosition.Value >= target.Position.Value)
                            await cts.CancelAsync();
                        break;

                    case CaughtUpCheckpoint: await cts.CancelAsync(); break;
                }
            }
        }

        // Assert — everything discarded, yet a synthetic checkpoint landed exactly at the target
        // head. The server matched (and never checkpointed) the target run, so only client synthesis
        // puts a watermark there — that exact-position checkpoint is the proof the hybrid synthesizes.
        await Assert.That(deliveredRecords).IsEqualTo(0);
        await Assert.That(checkpoints).IsNotEmpty();
        await Assert.That(checkpoints.Any(cp => cp.Positions[0].LogPosition.Value == target.Position.Value)).IsTrue();

        for (var i = 1; i < checkpoints.Count; i++)
            await Assert.That(checkpoints[i].Positions[0].LogPosition.Value)
                .IsGreaterThanOrEqualTo(checkpoints[i - 1].Positions[0].LogPosition.Value);
    }

    [Test]
    public async ValueTask stream_subscription_with_refinement_emits_synthetic_checkpoints(CancellationToken ct) {
        // Arrange — `stream = 'X' AND <predicate>` routes to a single-stream subscription, which
        // gets NO server checkpoints at all, yet the predicate discards client-side. `log_position
        // < 0` is never true, so every delivered record is discarded — pure invisible progress.
        // With window = 5, the SDK must still report scan progress at the configured cadence.
        var target = await Fixture.SeedTestStream(20, ct);

        const int window = 5;
        var        filter = RecordFilter.ByExpression($"stream = '{target.Stream}' AND log_position < 0");

        var checkpoints      = new List<FilterCheckpoint>();
        var deliveredRecords = 0;

        // Act — iterate to the live boundary; the stream sub catches up after all 20 are scanned.
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start           = RecordPosition.Earliest,
                               Filter          = filter,
                               FilterMaxSearchWindow = window,
                               SkipDecoding    = true,
                               ResolveLinks    = false
                           }, cts.Token).ConfigureAwait(false)) {
                switch (msg.Value) {
                    case Record:               deliveredRecords++;      break;
                    case FilterCheckpoint fc:  checkpoints.Add(fc);     break;
                    case CaughtUpCheckpoint:   await cts.CancelAsync();  break; // reached live end
                }
            }
        }

        // Assert — everything discarded, yet the watermark advanced via synthesized checkpoints at
        // roughly one per window over the 20 scanned records, monotonically.
        var perWindow = target.Records.Length / window;

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
    public async ValueTask pure_server_filter_without_refinement_synthesizes_nothing(CancellationToken ct) {
        // Arrange — ByStream is a pure server-routable filter with no client refinement: every
        // delivered record matches, so the SDK discards nothing and must synthesize nothing. It
        // routes to a single-stream subscription, which carries no server filter checkpoints, so
        // any FilterCheckpoint at all would be a synthesis the client should not have produced.
        var target        = await Fixture.SeedTestStream(5, ct);
        var expectedCount = target.Records.Length;

        var checkpoints = new List<Checkpoint>();

        // Act
        var records = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = RecordPosition.Earliest,
                Filter          = RecordFilter.ByStream(target.Stream),
                FilterMaxSearchWindow = 1,
                SkipDecoding    = true
            }, ct)
            .Records(checkpoints.Add, ct)
            .Take(expectedCount)
            .ToListAsync(ct);

        // Assert — records arrive; zero synthetic checkpoints. With window = 1 a stray synthesis
        // would emit one per record, so an empty result is a strong guard.
        await Assert.That(records.Count).IsEqualTo(expectedCount);
        await Assert.That(checkpoints.OfType<FilterCheckpoint>()).IsEmpty();
    }

    [Test]
    public async ValueTask synthetic_checkpoint_cadence_follows_the_configured_window(CancellationToken ct) {
        // Arrange — a match-everything client-side filter so every scanned record is delivered AND
        // counted. With one checkpoint per window, the count over N delivered records is ~N/window
        // — this proves the cadence, not merely that checkpoints exist. Load-robust: foreign
        // records also match and count, and we take a fixed number regardless of who wrote them.
        var anchor = await Fixture.LatestRecordPosition(ct);
        await Fixture.SeedTestStream(30, ct); // guarantee enough records ahead of the anchor

        const int window = 5;
        const int  take   = 20;

        var checkpoints = new List<Checkpoint>();

        // Act
        var records = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start           = anchor,
                Filter          = MatchAllClientSide(),
                FilterMaxSearchWindow = window,
                SkipDecoding    = true,
                ResolveLinks    = false
            }, ct)
            .Records(checkpoints.Add, ct)
            .Take(take)
            .ToListAsync(ct);

        // Assert — one synthetic checkpoint per `window` records. The boundary checkpoint at
        // exactly `take` is not pulled (Take stops at the record before it), so the count is
        // take/window, give or take one.
        var perWindow = take / window;

        await Assert.That(records.Count).IsEqualTo(take);
        await Assert.That(checkpoints.OfType<FilterCheckpoint>().Count())
            .IsGreaterThanOrEqualTo(perWindow - 1).And
            .IsLessThanOrEqualTo(perWindow);
    }
}
