using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class CreateIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async Task creates_a_new_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task creates_index_and_retrieves_it(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldNotThrowOrFailAsync(index => {
                index.Name.ShouldBe(indexName);
                index.Filter.ShouldBe("rec => rec.schema.name == 'OrderCreated'");
                index.Fields.Count.ShouldBe(1);
                index.Fields[0].Name.ShouldBe("amount");
                index.Fields[0].Selector.ShouldBe("rec => rec.value.amount");
                index.Fields[0].Type.ShouldBe(IndexFieldType.Double);
            });
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task fails_to_create_index_when_index_already_exists(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'SomethingElse'", fields, cancellationToken: ct)
            .ShouldFailAsync(error =>
                error.Value.ShouldBeOfType<AlreadyExistsException>());
    }
}
