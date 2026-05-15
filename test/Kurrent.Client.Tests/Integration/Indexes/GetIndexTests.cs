using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
[Skip("Flaky in CI.")]
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

        await KurrentDBFixture.WaitForEventualConsistency(ct);

        var index = await Fixture.Client.Indexes.Get(indexName, ct);

        await Assert.That(index.Name).IsEqualTo(indexName);
        await Assert.That(index.Filter).IsEqualTo("rec => rec.schema.name == 'ProductCreated'");
        await Assert.That(index.Fields).Count().IsEqualTo(1);
        await Assert.That(index.Fields[0].Name).IsEqualTo("price");
        await Assert.That(index.Fields[0].Type).IsEqualTo(IndexFieldType.Int64);
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_get_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Assert.That(async () => await Fixture.Client.Indexes
            .Get(indexName, ct))
            .Throws<NotFoundException>();
    }
}
