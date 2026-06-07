using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
[Timeout(20_000)]
public class CreateIndexTests {
    [ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask creates_a_new_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes.Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct);
    }

    [Test]
    public async ValueTask creates_index_and_retrieves_it(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct);

        await Eventually(async () => {
            var index = await Fixture.Client.Indexes.Get(indexName, ct);

            await Assert.That(index.Name).IsEqualTo(indexName);
            await Assert.That(index.Filter).IsEqualTo("rec => rec.schema.name == 'OrderCreated'");
            await Assert.That(index.Fields).Count().IsEqualTo(1);
            await Assert.That(index.Fields[0].Name).IsEqualTo("amount");
            await Assert.That(index.Fields[0].Selector).IsEqualTo("rec => rec.value.amount");
            await Assert.That(index.Fields[0].Type).IsEqualTo(IndexFieldType.Double);
        }, ct: ct);
    }

    [Test]
    public async ValueTask fails_to_create_index_when_index_already_exists(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct);

        await Assert.That(async () => await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'SomethingElse'", fields, cancellationToken: ct))
            .Throws<AlreadyExistsException>();
    }
}
