using Grpc.Core;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;

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

        await Fixture.Client.Indexes.Create(
            indexName,
            o => {
                o.Filter = "rec => rec.schema.name == 'OrderCreated'";
                o.Fields = fields;
                o.Start  = true;
            },
            ct);

        await Fixture.Client.Indexes.Stop(indexName, ct);

        await Assert.That(async () => {
            var stopped = await Fixture.Client.Indexes.Get(indexName, ct);
            await Assert.That(stopped.State).IsEqualTo(IndexState.Stopped);
        }).EventuallyPasses(RetryOnTransients(ct));

        await Fixture.Client.Indexes.Start(indexName, ct);

        await Assert.That(async () => {
            var started = await Fixture.Client.Indexes.Get(indexName, ct);
            await Assert.That(started.State).IsEqualTo(IndexState.Started);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_start_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var ex = await Assert.That(async () => await Fixture.Client.Indexes
            .Start(indexName, ct))
            .Throws<RpcException>();

        await Assert.That(ex!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_stop_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var ex = await Assert.That(async () => await Fixture.Client.Indexes
            .Stop(indexName, ct))
            .Throws<RpcException>();

        await Assert.That(ex!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask creates_index_without_starting(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("value", "rec => rec.value.value", IndexFieldType.Int32)
        };

        await Fixture.Client.Indexes.Create(
            indexName,
            o => {
                o.Filter = "rec => rec.schema.name == 'MetricRecorded'";
                o.Fields = fields;
                o.Start  = false;
            },
            ct);

        await Assert.That(async () => {
            var index = await Fixture.Client.Indexes.Get(indexName, ct);
            await Assert.That(index.State).IsEqualTo(IndexState.Stopped);
        }).EventuallyPasses(RetryOnTransients(ct));
    }
}
