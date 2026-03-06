using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;

namespace Kurrent.Client.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
public class ConnectorQueryTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

    [Test]
    public async ValueTask lists_connectors(CancellationToken ct) {
        var connectorId = KurrentClientTestFixture.NewConnectorId();
        var name        = KurrentClientTestFixture.NewConnectorName();
        var settings    = KurrentClientTestFixture.NewConnectorSettings();

        await Fixture.Client.Connectors
            .Create(connectorId, name, settings, ct);

        var options = new ConnectorListOptions { ConnectorIds = [connectorId] };

        var result = await Fixture.Client.Connectors
            .List(options, ct);

        await Assert.That(result.Items).IsNotEmpty();
        await Assert.That(result.Items).Contains(c => c.ConnectorId == connectorId);
    }
}
