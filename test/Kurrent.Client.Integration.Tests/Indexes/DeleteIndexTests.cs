using Google.Rpc.Exceptions;
using Kurrent.Client.Indexes;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Indexes;

[Category("Indexes")]
[Category("Integration")]
public class DeleteIndexTests {
    const int TestTimeoutMs = 20_000;

    [ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
    public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test, Timeout(TestTimeoutMs)]
    public async Task deletes_existing_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        var fields = new[] {
            new IndexField("amount", "rec => rec.value.amount", IndexFieldType.Double)
        };

        await Fixture.Client.Indexes
            .Create(indexName, "rec => rec.schema.name == 'OrderCreated'", fields, cancellationToken: ct)
            .ShouldNotThrowOrFailAsync();

        await Fixture.Client.Indexes
            .Delete(indexName, ct)
            .ShouldNotThrowOrFailAsync();
    }

    [Test, Timeout(TestTimeoutMs)]
    public async Task fails_to_delete_nonexistent_index(CancellationToken ct) {
        var indexName = $"test-index-{Guid.NewGuid():N}";

        await Fixture.Client.Indexes
            .Delete(indexName, ct)
            .ShouldFailAsync(error =>
                error.Value.ShouldBeOfType<NotFoundException>());
    }
}
