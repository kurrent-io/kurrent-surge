using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class StartStopIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask stops_and_starts_an_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, start: true, cancellationToken: ct);

        await Fixture.Client.Indexes.Stop(indexName, ct);

        var stopped = await Fixture.Client.Indexes.Get(indexName, ct);
        await Assert.That(stopped.State).IsEqualTo(IndexState.Stopped);

        await Fixture.Client.Indexes.Start(indexName, ct);

        var started = await Fixture.Client.Indexes.Get(indexName, ct);
        await Assert.That(started.State).IsEqualTo(IndexState.Started);
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_start_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Assert.That(async () => await Fixture.Client.Indexes
            .Start(indexName, ct))
            .Throws<NotFoundException>();
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_stop_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Assert.That(async () => await Fixture.Client.Indexes
            .Stop(indexName, ct))
            .Throws<NotFoundException>();
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask creates_index_without_starting(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("value", "rec => rec.value.value", IndexFieldType.Int32)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'MetricRecorded'", fields, start: false, cancellationToken: ct);

        var index = await Fixture.Client.Indexes.Get(indexName, ct);
        await Assert.That(index.State).IsEqualTo(IndexState.Stopped);
    }
}
