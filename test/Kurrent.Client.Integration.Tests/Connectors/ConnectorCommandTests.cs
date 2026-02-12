using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testing.Shouldly;

namespace Kurrent.Client.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
public class ConnectorCommandTests {
	[ClassDataSource<KurrentClientTestFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentClientTestFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask creates_connector_with_settings(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var name        = KurrentClientTestFixture.NewConnectorName();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask creates_connector_with_configuration(CancellationToken ct) {
        var connectorId   = KurrentClientTestFixture.NewConnectorId();
        var name          = KurrentClientTestFixture.NewConnectorName();
        var configuration = KurrentClientTestFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, name, configuration, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask deletes_existing_connector(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Connectors
			.Delete(connectorId, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask reconfigures_existing_connector(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct)
			.ShouldNotThrowOrFailAsync();

		var updatedSettings = new Dictionary<string, string?>(settings) {
			["subscription:filter:expression"] = "updated-"
		};

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedSettings, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask reconfigures_existing_connector_with_configuration(CancellationToken ct) {
		var connectorId   = KurrentClientTestFixture.NewConnectorId();
		var configuration = KurrentClientTestFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, configuration, ct)
			.ShouldNotThrowOrFailAsync();

		var updatedSettings = new Dictionary<string, string?>(KurrentClientTestFixture.NewConnectorSettings()) {
			["subscription:filter:expression"] = "updated-"
		};
		var updatedConfiguration = updatedSettings.ToProtobufStruct();

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedConfiguration, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask renames_existing_connector(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var name        = KurrentClientTestFixture.NewConnectorName();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct)
			.ShouldNotThrowOrFailAsync();

		var newName = KurrentClientTestFixture.NewConnectorName();

		await Fixture.Client.Connectors
			.Rename(connectorId, newName, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask resets_connector_to_log_position(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Connectors
			.Reset(connectorId, logPosition: 0, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask starts_connector_from_log_position(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Connectors
			.Start(connectorId, logPosition: 0, ct)
			.ShouldNotThrowOrFailAsync();
	}

	[Test]
	public async ValueTask stops_running_connector(CancellationToken ct) {
		var connectorId = KurrentClientTestFixture.NewConnectorId();
		var settings    = KurrentClientTestFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Connectors
			.Start(connectorId, ct)
			.ShouldNotThrowOrFailAsync();

		await Fixture.Client.Connectors
			.Stop(connectorId, ct)
			.ShouldNotThrowOrFailAsync();
	}
}
