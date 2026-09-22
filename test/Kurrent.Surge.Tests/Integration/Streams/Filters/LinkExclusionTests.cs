using System.Text;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Tests.Streams;

/// <summary>
/// An expression filter travels as a server filter even when no column narrows the scan, so a
/// <c>$all</c> subscription or read never resolves the standard projections' links back into
/// duplicates. The fixture runs <c>$by_category</c> and <c>$by_event_type</c>, so every appended
/// record gains a <c>$ce-</c> and a <c>$et-</c> link — the exact condition that made an unfiltered
/// <c>$all</c> deliver each record three times.
/// </summary>
/// <remarks>
/// Every test owns a schema name unique to itself, so the <c>$et-{schema}</c> link stream it inspects
/// holds nothing but its own records. Each test waits for that link stream before it asserts on
/// delivery: the projections are eventually consistent, and asserting "delivered once" before the
/// links exist would pass against a log that has no links in it yet.
/// </remarks>
[Category("Streams")]
[Category("Filters")]
[Category("Integration")]
[Timeout(60_000)]
public class LinkExclusionTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    const int SeededPerSchema = 5;

    // Raw bytes because the schema name is the subject under test, and only the Bytes factory lets a
    // caller state it. Guid 'N' format carries no '-', so the stream's category is this call's prefix
    // alone and no other test shares it.
    async ValueTask<StreamName> SeedSchemas(int count, CancellationToken ct, params string[] schemas) {
        var stream  = StreamName.From($"linkexclusion{Guid.NewGuid():N}-{Guid.NewGuid():N}");
        var payload = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("{}"));

        var records = schemas
            .SelectMany(schema => Enumerable.Range(0, count).Select(_ => AppendRecord.Create(payload, SchemaName.From(schema))))
            .ToArray();

        await Fixture.Client.Streams.AppendStream(stream, records, ct);

        return stream;
    }

    static string UniqueSchema(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    // Blocks until $et-{schema} holds `expected` link records, then returns them resolved: a stream
    // route resolves, so each record is the origin and carries the link that delivered it.
    async ValueTask<List<Record>> ReadSchemaLinks(string schema, int expected, CancellationToken ct) {
        var etStream = StreamName.From($"$et-{schema}");
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(45);
        var links    = new List<Record>();

        while (true) {
            links.Clear();

            try {
                await foreach (var msg in Fixture.Client.Streams.ReadStream(etStream,
                                   new ReadStreamOptions {
                                       Start        = Revision.Min,
                                       Direction    = ReadDirection.Forwards,
                                       Limit        = expected,
                                       SkipDecoding = true
                                   }, ct)) {
                    if (msg.Value is Record record)
                        links.Add(record);
                }
            }
            catch (StreamNotFoundException) {
                // The $by_event_type projection has not materialized $et-{schema} yet.
            }

            if (links.Count >= expected || DateTime.UtcNow >= deadline)
                return links;

            await Task.Delay(500, ct);
        }
    }

    // Catching up is the bound: a duplicate has to arrive before the live end, so an exact count
    // afterwards is a real "exactly once" rather than a Take that stopped early.
    async ValueTask<List<Record>> CollectUntilCaughtUp(RecordFilter filter, RecordPosition anchor, CancellationToken ct) {
        var delivered = new List<Record>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await foreach (var msg in Fixture.Client.Streams.Subscribe(new SubscribeOptions {
                           Start        = anchor,
                           Filter       = filter,
                           SkipDecoding = true
                       }, cts.Token).ConfigureAwait(false)) {
            switch (msg.Value) {
                case Record record:      delivered.Add(record);   break;
                case CaughtUpCheckpoint: await cts.CancelAsync(); break;
            }
        }

        return delivered;
    }

    [Test]
    public async ValueTask projections_wrote_links_for_the_appended_schema(CancellationToken ct) {
        // Arrange
        var schema = UniqueSchema("LinkProof");

        await SeedSchemas(SeededPerSchema, ct, schema);

        // Act
        var links = await ReadSchemaLinks(schema, SeededPerSchema, ct);

        // Assert — the fixture reproduces the condition the fix defends against: a link record per
        // appended record. The stream route resolves each one, so the record carries its link.
        var expectedLinkStream = StreamName.From($"$et-{schema}");

        await Assert.That(links.Count).IsEqualTo(SeededPerSchema);

        foreach (var link in links) {
            await Assert.That(link.Link.IsUnset).IsFalse();
            await Assert.That(link.Link.Position.Stream).IsEqualTo(expectedLinkStream);
        }
    }

    [Test]
    public async ValueTask subscribe_with_schema_in_filter_delivers_each_record_once(CancellationToken ct) {
        // Arrange
        var first  = UniqueSchema("InFirst");
        var second = UniqueSchema("InSecond");
        var anchor = await Fixture.LatestRecordPosition(ct);

        await SeedSchemas(SeededPerSchema, ct, first, second);
        await ReadSchemaLinks(first, SeededPerSchema, ct);
        await ReadSchemaLinks(second, SeededPerSchema, ct);

        var filter   = RecordFilter.ByExpression($"schema_name IN ('{first}', '{second}')");
        var expected = SeededPerSchema * 2;

        // Act
        var delivered = await CollectUntilCaughtUp(filter, anchor, ct);

        // Assert
        await Assert.That(delivered.Count).IsEqualTo(expected);
        await Assert.That(delivered.Select(r => r.Position.LogPosition.Value).Distinct().Count()).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask subscribe_with_schema_like_filter_delivers_each_record_once(CancellationToken ct) {
        // Arrange
        var schema = UniqueSchema("LikeOnce");
        var anchor = await Fixture.LatestRecordPosition(ct);

        await SeedSchemas(SeededPerSchema, ct, schema);
        await ReadSchemaLinks(schema, SeededPerSchema, ct);

        var filter = RecordFilter.ByExpression($"schema_name LIKE '{schema}%'");

        // Act
        var delivered = await CollectUntilCaughtUp(filter, anchor, ct);

        // Assert
        await Assert.That(delivered.Count).IsEqualTo(SeededPerSchema);
        await Assert.That(delivered.Select(r => r.Position.LogPosition.Value).Distinct().Count()).IsEqualTo(SeededPerSchema);
    }

    [Test]
    public async ValueTask subscribe_with_unroutable_expression_excludes_link_records(CancellationToken ct) {
        // Arrange — an OR across two columns derives no server constraint, so the whole expression runs
        // client-side. This is the shape that produced three deliveries per record.
        var schema = UniqueSchema("UnroutableSub");
        var anchor = await Fixture.LatestRecordPosition(ct);

        var stream = await SeedSchemas(SeededPerSchema, ct, schema);
        await ReadSchemaLinks(schema, SeededPerSchema, ct);

        var filter = RecordFilter.ByExpression($"stream = '{stream}' OR schema_name = 'no-such-schema'");

        // Act
        var delivered = await CollectUntilCaughtUp(filter, anchor, ct);

        // Assert
        await Assert.That(delivered.Count).IsEqualTo(SeededPerSchema);
        await Assert.That(delivered.Select(r => r.Position.LogPosition.Value).Distinct().Count()).IsEqualTo(SeededPerSchema);
    }

    [Test]
    public async ValueTask read_with_unroutable_expression_excludes_link_records(CancellationToken ct) {
        // Arrange — the read counterpart. A forwards $all read from the anchor ends at the log tail, so
        // it is bounded without a limit.
        var schema = UniqueSchema("UnroutableRead");
        var anchor = await Fixture.LatestRecordPosition(ct);

        var stream = await SeedSchemas(SeededPerSchema, ct, schema);
        await ReadSchemaLinks(schema, SeededPerSchema, ct);

        var filter = RecordFilter.ByExpression($"stream = '{stream}' OR schema_name = 'no-such-schema'");

        // Act
        var delivered = await Fixture.Client.Streams
            .ReadRecords(new ReadRecordsOptions {
                Start        = anchor.LogPosition,
                Filter       = filter,
                SkipDecoding = true
            }, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(delivered.Count).IsEqualTo(SeededPerSchema);
        await Assert.That(delivered.All(record => record.Stream == stream)).IsTrue();
    }
}
