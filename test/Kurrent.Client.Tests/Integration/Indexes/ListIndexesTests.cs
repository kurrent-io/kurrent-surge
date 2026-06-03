using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class ListIndexesTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask lists_indexes(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("total", "rec => rec.value.total", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'InvoiceCreated'", fields, cancellationToken: ct);

        await Eventually(async () => {
            var indexes = await Fixture.Client.Indexes.List(ct);

            await Assert.That(indexes).Contains(i => i.Name == indexName);
        }, ct);
    }
}
