using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Experimental;

namespace Kurrent.Surge.Streaming.Tests;

[Category("Consumer")]
[Category("Integration")]
[Timeout(30_000)]
public class ConsumerTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    public class RecordCounts : TestCaseGenerator<int> {
        protected override IEnumerable<int> Data() => [1, 5, 20];
    }

    [Test]
    [RecordCounts]
    public async ValueTask consumes_seeded_records_in_order(int count, CancellationToken ct) {
        // Arrange
        var data        = await Fixture.SeedTestStream(count, ct);
        var expectedIds = data.Records.Select(r => r.RecordId).ToArray();

        await using var consumer = new Consumer(CreateClientOptions());

        var options = new ConsumerOptions {
            // Equality routes to a single-stream subscription; Start defaults to Latest, so
            // catch-up over the seeded records needs an explicit Earliest.
            Filter       = RecordFilter.ByExpression($"stream = '{data.Stream}'"),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        var records = new List<Record>();

        // Act — a subscription never completes on its own; stop once all seeded records arrived
        await foreach (var record in consumer.Consume(options, ct)) {
            records.Add(record);

            if (records.Count == expectedIds.Length)
                break;
        }

        // Assert
        await Assert.That(records).Count().IsEqualTo(expectedIds.Length);

        for (var i = 0; i < expectedIds.Length; i++)
            await Assert.That(records[i].Id).IsEqualTo(expectedIds[i]);
    }

    [Test]
    [RecordCounts]
    public async ValueTask consumes_records_appended_after_subscribing(int count, CancellationToken ct) {
        // Arrange — the stream does not exist yet when consumption starts; Earliest makes
        // delivery deterministic whether the subscription wins or loses the race with the append
        var data        = await Fixture.TestData.GenerateSet(count, ct);
        var expectedIds = data.Records.Select(r => r.RecordId).ToArray();

        await using var consumer = new Consumer(CreateClientOptions());

        var options = new ConsumerOptions {
            Filter       = RecordFilter.ByExpression($"stream = '{data.Stream}'"),
            Start        = RecordPosition.Earliest,
            SkipDecoding = true
        };

        var records = new List<Record>();

        var consuming = Task.Run(async () => {
            await foreach (var record in consumer.Consume(options, ct)) {
                records.Add(record);

                if (records.Count == expectedIds.Length)
                    break;
            }
        }, ct);

        // Act
        await Fixture.Client.Streams.AppendStream(data.Stream, data.Records, ct);

        await consuming.WaitAsync(ct);

        // Assert
        await Assert.That(records).Count().IsEqualTo(expectedIds.Length);

        for (var i = 0; i < expectedIds.Length; i++)
            await Assert.That(records[i].Id).IsEqualTo(expectedIds[i]);
    }

    KurrentClientOptions CreateClientOptions() =>
        KurrentClientOptions.Parse(Fixture.ConnectionString).With(opts => {
            opts.LoggerFactory = Fixture.LoggerFactory;
            opts.Schema        = KurrentClientSchemaOptions.NoValidation;
            opts.Resilience    = KurrentClientResilienceOptions.NoResilience;
            opts.Messages.UseTestkitCatalog();
        });
}
