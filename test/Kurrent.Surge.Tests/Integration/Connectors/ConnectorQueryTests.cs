using Kurrent.Surge.Client.Connectors;
using Kurrent.Surge.Tests.Fixtures;

namespace Kurrent.Surge.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
[Timeout(10_000)]
public class ConnectorQueryTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask lists_connectors(CancellationToken ct) {
        var connectorId = KurrentDBFixture.NewConnectorId();
        var name        = KurrentDBFixture.NewConnectorName();
        var settings    = KurrentDBFixture.NewConnectorSettings();

        await Fixture.Client.Connectors
            .Create(connectorId, name, settings, ct);

        var options = new ConnectorListOptions { ConnectorIds = [connectorId] };

        await Assert.That(async () => {
            var result = await Fixture.Client.Connectors
                .List(options, ct);

            await Assert.That(result.Items).IsNotEmpty();
            await Assert.That(result.Items).Contains(c => c.ConnectorId == connectorId);
        }).EventuallyPasses(RetryOnTransients(ct));
    }
}
