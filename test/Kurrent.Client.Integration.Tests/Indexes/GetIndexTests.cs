using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class GetIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async Task gets_existing_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("price", "rec => rec.value.price", IndexFieldType.Int64)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'ProductCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldNotThrowOrFailAsync(index => {
                index.Name.ShouldBe(indexName);
                index.Filter.ShouldBe("rec => rec.schema.name == 'ProductCreated'");
                index.Fields.Count.ShouldBe(1);
                index.Fields[0].Name.ShouldBe("price");
                index.Fields[0].Type.ShouldBe(IndexFieldType.Int64);
            });
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task fails_to_get_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Fixture.Client.Indexes
            .Get(indexName, ct)
            .ShouldFailAsync(error =>
                error.Value.ShouldBeOfType<NotFoundException>());
    }
}
