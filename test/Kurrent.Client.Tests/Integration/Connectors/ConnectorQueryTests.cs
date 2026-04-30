using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
public class ConnectorQueryTests {
	[ClassDataSource<KurrentDBTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask lists_connectors(CancellationToken ct) {
        var connectorId = KurrentDBTestFixture.NewConnectorId();
        var name        = KurrentDBTestFixture.NewConnectorName();
        var settings    = KurrentDBTestFixture.NewConnectorSettings();

        await Fixture.Client.Connectors
            .Create(connectorId, name, settings, ct);

        var options = new ConnectorListOptions { ConnectorIds = [connectorId],  };

        var result = await Fixture.Client.Connectors
            .List(options, ct);

        await Assert.That(result.Items).IsNotEmpty();
        await Assert.That(result.Items).Contains(c => c.ConnectorId == connectorId);
    }
}
