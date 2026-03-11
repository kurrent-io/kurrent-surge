using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class ListIndexesTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async Task lists_indexes(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("total", "rec => rec.value.total", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'InvoiceCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .List(ct)
            .ShouldNotThrowOrFailAsync(indexes => {
                indexes.ShouldContain(i => i.Name == indexName);
            });
    }
}
