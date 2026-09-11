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
