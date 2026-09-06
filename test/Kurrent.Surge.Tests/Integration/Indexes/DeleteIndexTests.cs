using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Indexes;

namespace Kurrent.Surge.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class DeleteIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask deletes_existing_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct);

        await Fixture.Client.Indexes.Delete(indexName, ct);
    }

    [Test, Timeout(TestTimeoutMs)]
    public async ValueTask fails_to_delete_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var ex = await Assert.That(async () => await Fixture.Client.Indexes
            .Delete(indexName, ct))
            .Throws<IndexNotFoundException>();

        await Assert.That(ex!.Index).IsEqualTo(indexName);
    }
}
