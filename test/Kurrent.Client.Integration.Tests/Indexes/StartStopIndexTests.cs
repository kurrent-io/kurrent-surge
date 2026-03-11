using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class StartStopIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async Task stops_and_starts_an_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, start: true, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Stop(indexName, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldNotThrowOrFailAsync(index =>
                index.State.ShouldBe(IndexState.Stopped));

        await Fixture.Client.Indexes
            .Start(indexName, ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldNotThrowOrFailAsync(index =>
                index.State.ShouldBe(IndexState.Started));
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task fails_to_start_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Fixture.Client.Indexes
            .Start(indexName, ct)
            .ShouldFailAsync(error =>
                error.Value.ShouldBeOfType<NotFoundException>());
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task fails_to_stop_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Fixture.Client.Indexes
            .Stop(indexName, ct)
            .ShouldFailAsync(error =>
                error.Value.ShouldBeOfType<NotFoundException>());
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task creates_index_without_starting(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("value", "rec => rec.value.value", IndexFieldType.Int32)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'MetricRecorded'", fields, start: false, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldNotThrowOrFailAsync(index =>
                index.State.ShouldBe(IndexState.Stopped));
    }
}
