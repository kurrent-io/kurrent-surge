using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Sdk.Testkit.Fixtures;

namespace Kurrent.Client.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
public class ConnectorCommandTests {
	[ClassDataSource<KurrentDBTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask creates_connector_with_settings(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var name        = KurrentDBTestFixture.NewConnectorName();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct);
	}

	[Test]
	public async ValueTask creates_connector_with_configuration(CancellationToken ct) {
        var connectorId   = KurrentDBTestFixture.NewConnectorId();
        var name          = KurrentDBTestFixture.NewConnectorName();
        var configuration = KurrentDBTestFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, name, configuration, ct);
	}

	[Test]
	public async ValueTask deletes_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Delete(connectorId, ct);
	}

	[Test]
	public async ValueTask reconfigures_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		var updatedSettings = new Dictionary<string, string?>(settings) {
			["subscription:filter:expression"] = "updated-"
		};

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedSettings, ct);
	}

	[Test]
	public async ValueTask reconfigures_existing_connector_with_configuration(CancellationToken ct) {
		var connectorId   = KurrentDBTestFixture.NewConnectorId();
		var configuration = KurrentDBTestFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, configuration, ct);

		var updatedSettings = new Dictionary<string, string?>(KurrentDBTestFixture.NewConnectorSettings()) {
			["subscription:filter:expression"] = "updated-"
		};
		var updatedConfiguration = updatedSettings.ToProtobufStruct();

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedConfiguration, ct);
	}

	[Test]
	public async ValueTask renames_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var name        = KurrentDBTestFixture.NewConnectorName();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct);

		var newName = KurrentDBTestFixture.NewConnectorName();

		await Fixture.Client.Connectors
			.Rename(connectorId, newName, ct);
	}

	[Test]
	public async ValueTask resets_connector_to_log_position(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Reset(connectorId, logPosition: 0, ct);
	}

	[Test]
	public async ValueTask starts_connector_from_log_position(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Start(connectorId, logPosition: 0, ct);
	}

	[Test]
	public async ValueTask stops_running_connector(CancellationToken ct) {
		var connectorId = KurrentDBTestFixture.NewConnectorId();
		var settings    = KurrentDBTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Start(connectorId, ct);

		await Fixture.Client.Connectors
			.Stop(connectorId, ct);
	}
}
