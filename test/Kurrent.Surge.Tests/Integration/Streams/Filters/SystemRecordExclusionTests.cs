using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;

namespace Kurrent.Surge.Tests.Streams;

/// <summary>
/// An application read of the log never delivers a record the server or a projection wrote for
/// itself: any record whose stream or schema name starts with <c>$</c>. The default filter carries
/// that rule on both axes. <see cref="RecordFilter.None"/> is the raw log and delivers everything.
/// </summary>
/// <remarks>
/// A hard delete writes a <c>$streamDeleted</c> record into the user's own stream, synchronously.
/// That is the one system record a stream-name regex cannot see, so it is the probe for the
/// client-side half of the rule.
/// </remarks>
[Category("Streams")]
[Category("Filters")]
[Category("Integration")]
[Timeout(60_000)]
public class SystemRecordExclusionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    const string TombstoneSchema = "$streamDeleted";

    // Catching up is the bound: every record written before the subscription started sits before
    // the live end, so an exact count afterwards is a real "never delivered" rather than a Take
    // that stopped early.
    async ValueTask<List<Record>> CollectUntilCaughtUp(SubscribeOptions options, CancellationToken ct) {
        var delivered = new List<Record>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await foreach (var msg in Fixture.Client.Streams.Subscribe(options, cts.Token).ConfigureAwait(false)) {
            switch (msg.Value) {
                case Record record:      delivered.Add(record);   break;
                case CaughtUpCheckpoint: await cts.CancelAsync(); break;
            }
        }

        return delivered;
    }

    [Test]
    public async ValueTask default_filter_on_all_delivers_no_system_record(CancellationToken ct) {
        // Arrange — a tombstoned stream puts a $streamDeleted record inside a user stream past the
        // anchor, and the projections link every seeded record from $ce- and $et- streams.
        var anchor = await Fixture.LatestRecordPosition(ct);
        var victim = await Fixture.SeedTestStream(1, ct);

        await Fixture.Client.Streams.Tombstone(victim.Stream, victim.Revision, ct);

        var target = await Fixture.SeedTestStream(5, ct);

        var expectedFromTarget = target.Records.Length;

        // Act — default options: ExcludeSystemRecords on the $all route.
        var delivered = await CollectUntilCaughtUp(new SubscribeOptions {
            Start        = anchor,
            SkipDecoding = true
        }, ct);

        // Assert
        var fromTarget    = delivered.Count(record => record.Stream == target.Stream);
        var systemRecords = delivered
            .Where(record => record.Stream.Value.StartsWith('$') || record.Schema.Schema.Value.StartsWith('$'))
            .ToList();

        await Assert.That(fromTarget).IsEqualTo(expectedFromTarget);
        await Assert.That(systemRecords).IsEmpty();
    }

    [Test]
    public async ValueTask none_delivers_the_raw_log(CancellationToken ct) {
        // Arrange — the standard projections write a $> link for every seeded record.
        var anchor = await Fixture.LatestRecordPosition(ct);

        await Fixture.SeedTestStream(5, ct);

        var links = new List<Record>();

        // Act — the raw log, decoded; stop at the first link row.
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct)) {
            await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                               Start  = anchor,
                               Filter = RecordFilter.None
                           }, cts.Token).ConfigureAwait(false)) {
                if (msg.Value is not Record { IsLink: true } row)
                    continue;

                links.Add(row);
                await cts.CancelAsync();
            }
        }

        // Assert — the link row arrives unresolved, at its own position in its own $ stream, and
        // decodes to the pointer it carries.
        await Assert.That(links).IsNotEmpty();

        var link = links[0];

        var linkStream   = link.Stream.Value;
        var linkSchema   = link.Schema.Schema;
        var linkIsUnset  = link.Link.IsUnset;
        var linkPosition = link.CheckpointPosition;
        var linkValue    = link.Value;

        await Assert.That(linkStream.StartsWith('$')).IsTrue();
        await Assert.That(linkSchema).IsEqualTo(LinkTo.SchemaName);
        await Assert.That(linkIsUnset).IsTrue();
        await Assert.That(linkPosition).IsEqualTo(link.Position);
        await Assert.That(linkValue).IsTypeOf<LinkTo>();
    }

    [Test]
    public async ValueTask limit_read_behind_a_tombstone_delivers_the_last_user_record(CancellationToken ct) {
        // Arrange — the stream's tail row is its tombstone. The server's count is met by that row
        // and the client drops it, so the read must page on to the record behind it.
        var target = await Fixture.SeedTestStream(2, ct);

        await Fixture.Client.Streams.Tombstone(target.Stream, target.Revision, ct);

        var filter           = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'");
        var expectedPosition = RecordPosition.From(target.Position, target.Stream, target.Revision);

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = LogPosition.Latest,
                Direction    = ReadDirection.Backwards,
                Limit        = 1,
                Filter       = filter,
                SkipDecoding = true
            }, ct)
            .ToListAsync();

        // Assert
        await Assert.That(records.Count).IsEqualTo(1);

        var latest = records[0].Position;

        await Assert.That(latest).IsEqualTo(expectedPosition);
    }

    [Test]
    public async ValueTask default_limit_one_read_of_the_tail_delivers_a_user_record(CancellationToken ct) {
        // Arrange — the shape the Aspire health check reads with. A tombstone at the tail must not
        // turn a Limit = 1 read into an empty one.
        var target = await Fixture.SeedTestStream(1, ct);

        await Fixture.Client.Streams.Tombstone(target.Stream, target.Revision, ct);

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = LogPosition.Latest,
                Direction    = ReadDirection.Backwards,
                Limit        = 1,
                SkipDecoding = true
            }, ct)
            .ToListAsync();

        // Assert
        await Assert.That(records.Count).IsEqualTo(1);

        var latest       = records[0];
        var latestStream = latest.Stream.Value;
        var latestSchema = latest.Schema.Schema.Value;

        await Assert.That(latestStream.StartsWith('$')).IsFalse();
        await Assert.That(latestSchema.StartsWith('$')).IsFalse();
    }

    [Test]
    public async ValueTask forwards_limit_read_delivers_the_rows_past_a_dropped_one(CancellationToken ct) {
        // Arrange — A0, A's tombstone, B0, B1 in log order, narrowed to the two streams on the wire.
        // Limit 3 counts delivered rows, so the dropped tombstone must not shorten the result.
        var first = await Fixture.SeedTestStream(1, ct);

        await Fixture.Client.Streams.Tombstone(first.Stream, first.Revision, ct);

        var second = await Fixture.SeedTestStream(2, ct);

        var filter   = RecordFilter.ByExpression($"stream IN ('{first.Stream}', '{second.Stream}')");
        var expected = new List<RecordPosition> {
            RecordPosition.From(first.Position, first.Stream, Revision.Min),
        };

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = first.Position,
                Direction    = ReadDirection.Forwards,
                Limit        = 3,
                Filter       = filter,
                SkipDecoding = true
            }, ct)
            .ToListAsync();

        // Assert — three user rows, none repeated, the tombstone absent.
        var expectedStreams   = new List<string> { first.Stream.Value, second.Stream.Value };
        var expectedRevisions = new List<Revision> { Revision.Min, Revision.From(1) };

        var positions = records.Select(record => record.Position).ToList();
        var streams   = records.Select(record => record.Stream.Value).Distinct().ToList();
        var revisions = records.Where(record => record.Stream == second.Stream).Select(record => record.Revision).ToList();
        var schemas   = records.Select(record => record.Schema.Schema.Value).ToList();

        await Assert.That(records.Count).IsEqualTo(3);
        await Assert.That(positions[0]).IsEqualTo(expected[0]);
        await Assert.That(streams).IsEquivalentTo(expectedStreams);
        await Assert.That(revisions).IsEquivalentTo(expectedRevisions);
        await Assert.That(schemas.Any(schema => schema.StartsWith('$'))).IsFalse();
    }

    [Test]
    public async ValueTask limit_counts_delivered_matches_on_a_client_evaluated_stream_read(CancellationToken ct) {
        // Arrange — a stream route with a residual the server cannot apply: the client rejects
        // revisions 0..2, so a Limit of 2 must read through the rejected head and deliver 3 and 4.
        var target = await Fixture.SeedTestStream(5, ct);

        var filter   = RecordFilter.ByExpression($"stream = '{target.Stream}' AND revision >= 3");
        var expected = new List<Revision> { Revision.From(3), Revision.From(4) };

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = RecordPosition.Earliest,
                Direction    = ReadDirection.Forwards,
                Limit        = 2,
                Filter       = filter,
                SkipDecoding = true
            }, ct)
            .ToListAsync();

        // Assert
        var revisions = records.Select(record => record.Revision).ToList();

        await Assert.That(revisions).IsEquivalentTo(expected);
    }

    [Test]
    public async ValueTask backwards_stream_read_with_a_residual_matching_nothing_is_empty(CancellationToken ct) {
        // Arrange — a residual no row satisfies: the read walks the whole stream, delivers nothing,
        // and ends at the first revision.
        var target = await Fixture.SeedTestStream(5, ct);

        var filter = RecordFilter.ByExpression($"stream = '{target.Stream}' AND revision < 0");

        // Act
        var records = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Direction    = ReadDirection.Backwards,
                Limit        = 1,
                Filter       = filter,
                SkipDecoding = true
            }, ct)
            .ToListAsync();

        // Assert
        await Assert.That(records).IsEmpty();
    }

    [Test]
    public async ValueTask user_stream_regex_on_all_drops_system_types_client_side(CancellationToken ct) {
        // Arrange — the user's regex takes the wire slot, so the server passes every record of the
        // stream, tombstone included; only the client can drop it by type.
        var anchor = await Fixture.LatestRecordPosition(ct);
        var target = await Fixture.SeedTestStream(5, ct);

        await Fixture.Client.Streams.Tombstone(target.Stream, target.Revision, ct);

        var filter        = RecordFilter.ByExpression($"stream LIKE '{target.Stream}%'");
        var expectedCount = target.Records.Length;

        // Act
        var delivered = await CollectUntilCaughtUp(new SubscribeOptions {
            Start        = anchor,
            Filter       = filter,
            SkipDecoding = true
        }, ct);

        // Assert — the records arrive, the tombstone never does.
        var tombstones = delivered.Where(record => record.Schema.Schema.Value == TombstoneSchema).ToList();

        await Assert.That(delivered.Count).IsEqualTo(expectedCount);
        await Assert.That(tombstones).IsEmpty();
    }
}
