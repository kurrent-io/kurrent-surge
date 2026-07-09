using Grpc.Core;
using Kurrent.Surge.Client.Indexes;
using Kurrent.Surge.Tests.Fixtures;

namespace Kurrent.Surge.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class GetIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask gets_existing_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("price", "rec => rec.value.price", IndexFieldType.Int64)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'ProductCreated'", fields, cancellationToken: ct);

        await Assert.That(async () => {
            var index = await Fixture.Client.Indexes.Get(indexName, ct);

            await Assert.That(index.Name).IsEqualTo(indexName);
            await Assert.That(index.Filter).IsEqualTo("rec => rec.schema.name == 'ProductCreated'");
            await Assert.That(index.Fields).Count().IsEqualTo(1);
            await Assert.That(index.Fields[0].Name).IsEqualTo("price");
            await Assert.That(index.Fields[0].Type).IsEqualTo(IndexFieldType.Int64);
        }).EventuallyPasses(RetryOnTransients(ct));
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_get_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var ex = await Assert.That(async () => await Fixture.Client.Indexes
            .Get(indexName, ct))
            .Throws<RpcException>();

        await Assert.That(ex!.StatusCode).IsEqualTo(StatusCode.NotFound);
    }
}
