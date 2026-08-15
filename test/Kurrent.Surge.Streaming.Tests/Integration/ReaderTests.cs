using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Reader")]
[Category("Integration")]
[Timeout(30_000)]
public class ReaderTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    public class RecordCounts : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [1, 5, 20];
    }

    [Test]
    [RecordCounts]
    public async ValueTask reads_appended_records_in_order(int count, CancellationToken ct) {
        // Arrange — pin Start to the current log tail: the LIKE filter keeps this read on the
        // $all log path, and an unbounded scan from Earliest grows with the shared container's
        // whole history instead of this test's data.
        var from        = await Fixture.LatestRecordPosition(ct);
        var data        = await Fixture.SeedTestStream(count, ct);
        var expectedIds = data.Records.Select(r => r.RecordId).ToArray();

        await using var reader = new Reader(CreateClientOptions());

        var options = new ReadRecordsOptions {
            // LIKE keeps this on the LOG path ($all + StreamRegexFilter); equality would route to a stream read.
            Filter       = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            Start        = from,
            SkipDecoding = true
        };

        // Act
        var records = await reader.Read(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records).Count().IsEqualTo(expectedIds.Length);

        for (var i = 0; i < expectedIds.Length; i++)
            await Assert.That(records[i].Id).IsEqualTo(expectedIds[i]);
    }

    public class LimitCases : TestCaseGenerator<int, int> {
        protected override IEnumerable<(int, int)> Data() => [
            (10, 4),
            (5, 1),
            (8, 8)
        ];
    }

    [Test]
    [LimitCases]
    public async ValueTask respects_read_limit(int count, int limit, CancellationToken ct) {
        // Arrange — Start pinned to the log tail for the same reason as above: bounded scan.
        var from        = await Fixture.LatestRecordPosition(ct);
        var data        = await Fixture.SeedTestStream(count, ct);
        var expectedIds = data.Records.Take(limit).Select(r => r.RecordId).ToArray();

        await using var reader = new Reader(CreateClientOptions());

        var options = new ReadRecordsOptions {
            Filter       = RecordFilter.ByExpression($"stream LIKE '{data.Stream}%'"),
            Start        = from,
            Limit        = limit,
            SkipDecoding = true
        };

        // Act
        var records = await reader.Read(options, ct)
            .Where(msg => msg.IsRecord)
            .Select(msg => msg.AsRecord)
            .ToListAsync(ct);

        // Assert
        await Assert.That(records).Count().IsEqualTo(expectedIds.Length);

        for (var i = 0; i < expectedIds.Length; i++)
            await Assert.That(records[i].Id).IsEqualTo(expectedIds[i]);
    }

    KurrentClientOptions CreateClientOptions() =>
        KurrentClientOptions.Parse(Fixture.ConnectionString).With(opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Schema        = KurrentClientSchemaOptions.AutoRegister;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseTestkitCatalog();
        });
}
