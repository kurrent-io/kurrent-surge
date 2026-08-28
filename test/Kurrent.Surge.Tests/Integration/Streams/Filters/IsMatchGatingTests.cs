using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Tests.Streams;

/// <summary>
/// <see cref="RecordFilter.RequiresClientEvaluation"/> is the sole authority on whether the SDK runs
/// <see cref="RecordFilter.IsMatch"/> on delivered records. When it is <c>false</c> the server filter
/// is authoritative: the SDK delivers the server's output as-is — in the resolution form the caller
/// asked for — and never re-applies a client predicate. When it is <c>true</c> the filter carries a
/// residual the server could not apply, so <c>IsMatch</c> must still run. Identical on read and
/// subscribe.
/// </summary>
/// <remarks>
/// The two index shapes behave differently, both verified against the server:
/// <list type="bullet">
/// <item><c>$idx-ce-{cat}</c> (<see cref="RecordFilter.ByCategory"/>) is the secondary index: the
/// server delivers the <b>resolved origin</b> events, whose category equals the filter's — so
/// <c>IsMatch</c> would pass anyway and the gate is a safety net.</item>
/// <item><c>$ce-{cat}</c> (<see cref="RecordFilter.ByStream"/> over a real category link stream) with
/// link resolution delivers resolved origins whose <b>stream is the origin</b> (e.g. <c>cat-123</c>),
/// which the filter's <c>^\$ce-{cat}$</c> regex rejects — without the gate every record is silently
/// dropped. This is the load-bearing case.</item>
/// </list>
/// Each test seeds its OWN unique category so the <c>$ce-{cat}</c> and <c>$idx-ce-{cat}</c> streams it
/// reads are exclusively its own. The default test data source seeds everything into the shared
/// <c>race</c> category, which under parallel load floods those streams and makes a bounded read
/// non-deterministic — a unique category restores exact-count assertions without a firehose.
/// </remarks>
[Category("Streams")]
[Category("Filters")]
[Category("Integration")]
[Timeout(60_000)]
public class IsMatchGatingTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    /// <summary>
    /// Seeds <paramref name="count"/> generated records into a stream whose category is unique to this
    /// call, so the derived <c>$ce-{category}</c> and <c>$idx-ce-{category}</c> streams contain nothing
    /// but these records — the key to deterministic, load-robust assertions over them.
    /// </summary>
    async ValueTask<(StreamName Stream, string Category)> SeedUniqueCategory(int count, CancellationToken ct) {
        // Guid 'N' format has no '-', so the stream's category (text before the first '-') is exactly
        // this prefix and is shared by no other test.
        var category = $"dev1776{Guid.NewGuid():N}";
        var stream   = StreamName.From($"{category}-{Guid.NewGuid():N}");

        var data = await Fixture.TestData.GenerateSet(count, ct);
        await Fixture.Client.Streams.AppendStream(stream, data.Records, ct);

        return (stream, category);
    }

    [Test]
    public async ValueTask read_with_server_routable_filter_does_not_run_client_ismatch(CancellationToken ct) {
        // Arrange — ByStream over the real category link stream $ce-{cat}. It is server-routable
        // (RequiresClientEvaluation == false) and its IsMatch is the regex ^\$ce-{cat}$. With link
        // resolution ON the server returns resolved ORIGIN events (stream {cat}-{id}), which that regex
        // rejects — so without the gate every one is dropped.
        const int seeded = 10;

        var (stream, category) = await SeedUniqueCategory(seeded, ct);
        var ceStream = StreamName.From($"$ce-{category}");
        var filter   = (StreamNameFilter)RecordFilter.ByStream(ceStream);

        await Assert.That(filter.RequiresClientEvaluation).IsFalse();

        // Act — read $ce-{cat} with link resolution, gated. The category is ours alone, so the only
        // records are the seeded ones. Poll for the eventually-consistent category projection (the
        // $ce- stream does not exist until it runs). A wall-clock deadline rather than a fixed
        // attempt count, matching the sibling test below: a cold server needs well past 10s to
        // materialize the link stream, and the loop exits the instant it appears.
        var delivered = new List<Record>();
        var deadline  = DateTime.UtcNow + TimeSpan.FromSeconds(45);

        for (var attempt = 0; delivered.Count < seeded; attempt++) {
            if (attempt > 0) {
                if (DateTime.UtcNow >= deadline)
                    break;

                await Task.Delay(500, ct);
            }

            delivered.Clear();

            try {
                await foreach (var msg in Fixture.Client.Streams.ReadStream(ceStream,
                                   new ReadStreamOptions {
                                       Start = Revision.Min, Direction = ReadDirection.Forwards,
                                       Limit = seeded, ResolveLinks = true, SkipDecoding = true
                                   }, ct)) {
                    if (msg.Value is Record record)
                        delivered.Add(record);
                }
            }
            catch (StreamNotFoundException) {
                // Projection has not materialized $ce-{cat} yet — retry.
            }
        }

        // Assert — every record was delivered (pre-gate they would all be dropped), each is a resolved
        // origin on its own stream, and the filter's own predicate rejects it: proof IsMatch never gated.
        await Assert.That(delivered.Count).IsEqualTo(seeded);

        foreach (var record in delivered) {
            await Assert.That(record.IsLink).IsFalse();
            await Assert.That(record.Stream).IsEqualTo(stream);
            await Assert.That(filter.IsMatch(record)).IsFalse();
        }
    }

    [Test]
    public async ValueTask subscribe_with_server_routable_filter_does_not_run_client_ismatch(CancellationToken ct) {
        // Arrange — same server-routable $ce-{cat} filter on the subscribe path.
        const int seeded = 10;

        var (stream, category) = await SeedUniqueCategory(seeded, ct);
        var ceStream = StreamName.From($"$ce-{category}");
        var filter   = (StreamNameFilter)RecordFilter.ByStream(ceStream);

        await Assert.That(filter.RequiresClientEvaluation).IsFalse();

        // Act — subscribe from the start of our (exclusively-owned) $ce-{cat} stream, resolving links;
        // catch-up naturally waits out the projection lag. Bounded by Take to the seeded count.
        var delivered = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start = RecordPosition.Earliest, Filter = filter, ResolveLinks = true, SkipDecoding = true
            }, ct)
            .Records(_ => { }, ct)
            .Take(seeded)
            .ToListAsync(ct);

        // Assert — all delivered (dropped pre-gate), each a resolved origin the filter regex rejects.
        await Assert.That(delivered.Count).IsEqualTo(seeded);

        foreach (var record in delivered) {
            await Assert.That(record.IsLink).IsFalse();
            await Assert.That(record.Stream).IsEqualTo(stream);
            await Assert.That(filter.IsMatch(record)).IsFalse();
        }
    }

    [Test]
    public async ValueTask category_index_read_delivers_resolved_origin_records(CancellationToken ct) {
        // Arrange — the OTHER index shape: ByCategory targets the $idx-ce-{cat} secondary index. The
        // server delivers resolved ORIGIN events (verified: IsLink == false, stream == origin) even with
        // ResolveLinks = false, and their category equals the filter's — so here IsMatch passes anyway.
        // This pins the $idx- behavior that contrasts with the $ce- case above.
        const int seeded = 5;

        var anchor = await Fixture.LatestRecordPosition(ct);

        var (stream, category) = await SeedUniqueCategory(seeded, ct);
        var filter = RecordFilter.ByCategory(category);

        await Assert.That(filter.RequiresClientEvaluation).IsFalse();

        // Act — scan $all filtered to our unique index prefix from the pre-seed anchor (bounded result:
        // only our records carry this category). Poll for the eventually-consistent index.
        //
        // The $idx-ce-{cat} secondary index is eventually consistent, and its catch-up lag grows with the
        // total log volume the server has accumulated — on a saturated shared container it can trail well
        // past the 10s the $ce-{cat} link stream needs above. Poll on a wall-clock deadline (not a fixed
        // attempt count) so the wait scales to the lag, bounded safely under the 60s test timeout; the
        // loop exits the instant the index has caught up.
        var delivered = new List<Record>();
        var deadline  = DateTime.UtcNow + TimeSpan.FromSeconds(45);

        while (true) {
            delivered.Clear();

            await foreach (var msg in Fixture.Client.Streams.ReadRecords(
                               new ReadRecordsOptions {
                                   Start = anchor.LogPosition, Filter = filter,
                                   Limit = seeded, ResolveLinks = false, SkipDecoding = true
                               }, ct)) {
                if (msg.Value is Record record)
                    delivered.Add(record);
            }

            if (delivered.Count >= seeded || DateTime.UtcNow >= deadline)
                break;

            await Task.Delay(500, ct);
        }

        // Assert — the index delivers our records as resolved origins whose category matches the filter.
        await Assert.That(delivered.Count).IsEqualTo(seeded);

        foreach (var record in delivered) {
            await Assert.That(record.IsLink).IsFalse();
            await Assert.That(record.Stream).IsEqualTo(stream);
            await Assert.That(record.Stream.Category).IsEqualTo(category);
            await Assert.That(filter.IsMatch(record)).IsTrue();
        }
    }

    [Test]
    public async ValueTask expression_filter_with_refinement_still_evaluates_ismatch_on_both_paths(CancellationToken ct) {
        // Arrange — a hybrid filter on our own unique stream: `stream = 'X'` routes server-side to a
        // single-stream read/subscription, while `log_position >= P` is a residual the server cannot
        // apply there, so it runs client-side (RequiresClientEvaluation == true). The residual keeps the
        // tail and discards the head — proving IsMatch STILL gates delivery when the flag is true, i.e.
        // the gate does not over-apply.
        var target = await Fixture.SeedTestStream(6, ct);

        // Learn the absolute log positions in append order (monotonic with revision). The pivot is the
        // 4th record's position, so revisions 3..5 are kept and 0..2 discarded.
        var positions = await Fixture.Client.Streams
            .ReadStream(target.Stream, ct)
            .Where(msg => msg.IsRecord).Select(msg => msg.AsRecord)
            .Select(record => record.Position.LogPosition.Value)
            .ToListAsync(ct);

        await Assert.That(positions.Count).IsEqualTo(target.Records.Length);

        const int pivotIndex   = 3;
        var       pivot        = positions[pivotIndex];
        var       expectedKept = target.Records.Length - pivotIndex; // 3

        var filter = RecordFilter.ByExpression($"stream = '{target.Stream}' AND log_position >= {pivot}");

        await Assert.That(filter.RequiresClientEvaluation).IsTrue();

        // Act 1 — read path: every delivered record must satisfy the residual.
        var readDelivered = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start = RecordPosition.Earliest, Filter = filter,
                Limit = target.Records.Length, SkipDecoding = true
            }, ct)
            .Where(msg => msg.IsRecord).Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Act 2 — subscribe path: same residual, bounded by Take to the kept tail.
        var subscribeDelivered = await Fixture.Client.Streams
            .Subscribe(new SubscribeOptions {
                Start = RecordPosition.Earliest, Filter = filter, SkipDecoding = true
            }, ct)
            .Records(_ => { }, ct)
            .Take(expectedKept)
            .ToListAsync(ct);

        // Assert — both paths keep exactly the tail; the discarded head never leaks through, which it
        // would if the gate had wrongly skipped IsMatch.
        await Assert.That(readDelivered.Count).IsEqualTo(expectedKept);
        await Assert.That(readDelivered.All(record => record.Position.LogPosition.Value >= pivot)).IsTrue();

        await Assert.That(subscribeDelivered.Count).IsEqualTo(expectedKept);
        await Assert.That(subscribeDelivered.All(record => record.Position.LogPosition.Value >= pivot)).IsTrue();
    }
}
