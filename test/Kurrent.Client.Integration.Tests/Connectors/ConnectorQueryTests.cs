using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

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
            .Create(
                connectorId, name, settings,
                ct
            )
            .ShouldNotThrowOrFailAsync();

        var options = new ConnectorListOptions { ConnectorIds = [connectorId] };

        await Fixture.Client.Connectors
            .List(options, ct)
            .ShouldNotThrowOrFailAsync(result => {
                result.Items.ShouldNotBeEmpty();
                result.Items.ShouldContain(c => c.ConnectorId == connectorId);
            });
    }
}
