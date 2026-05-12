using Kurrent.Client.Connectors;
using Kurrent.Client.Tests.Fixtures;
using Kurrent.Testkit.Aspire;
using Microsoft.Extensions.Configuration;

namespace Kurrent.Client.Tests.Connectors;

[Category("Connectors")]
[Category("Integration")]
public class ConnectorCommandTests {
	[ClassDataSource<KurrentDBFixture>(Shared = SharedType.PerTestSession)]
	public required KurrentDBFixture Fixture { get; [UsedImplicitly] init; }

	[Test]
	public async ValueTask creates_connector_with_settings(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var name        = KurrentDBFixture.NewConnectorName();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct);
	}

	[Test]
	public async ValueTask creates_connector_with_configuration(CancellationToken ct) {
        var connectorId   = KurrentDBFixture.NewConnectorId();
        var name          = KurrentDBFixture.NewConnectorName();
        var configuration = KurrentDBFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, name, configuration, ct);
	}

	[Test]
	public async ValueTask creates_connector_with_json(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var name        = KurrentDBFixture.NewConnectorName();
		const string json = """
			{
				"instanceTypeName": "serilog-sink",
				"subscription": {
					"filter": { "scope": "stream", "filterType": "prefix", "expression": "test-" },
					"initialPosition": "latest"
				}
			}
			""";

		await Fixture.Client.Connectors
			.Create(connectorId, name, json, ct);
	}

	[Test]
	public async ValueTask deletes_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Delete(connectorId, ct);
	}

	[Test]
	public async ValueTask reconfigures_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		var updatedSettings = new Dictionary<string, string>(settings) {
			["subscription:filter:expression"] = "updated-"
		};

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedSettings, ct);
	}

	[Test]
	public async ValueTask reconfigures_existing_connector_with_configuration(CancellationToken ct) {
		var connectorId   = KurrentDBFixture.NewConnectorId();
		var configuration = KurrentDBFixture.NewConnectorConfiguration();

		await Fixture.Client.Connectors
			.Create(connectorId, configuration, ct);

		var updatedConfiguration = new ConfigurationBuilder()
			.AddInMemoryCollection(KurrentDBFixture.NewConnectorSettings()
				.Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value))
				.Append(new KeyValuePair<string, string?>("subscription:filter:expression", "updated-")))
			.Build();

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedConfiguration, ct);
	}

	[Test]
	public async ValueTask reconfigures_existing_connector_with_json(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		const string updatedJson = """
			{
				"instanceTypeName": "serilog-sink",
				"subscription": {
					"filter": { "scope": "stream", "filterType": "prefix", "expression": "updated-" },
					"initialPosition": "latest"
				}
			}
			""";

		await Fixture.Client.Connectors
			.Reconfigure(connectorId, updatedJson, ct);
	}

	[Test]
	public async ValueTask renames_existing_connector(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var name        = KurrentDBFixture.NewConnectorName();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, name, settings, ct);

		var newName = KurrentDBFixture.NewConnectorName();

		await Fixture.Client.Connectors
			.Rename(connectorId, newName, ct);
	}

	[Test]
	public async ValueTask resets_connector_to_log_position(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Reset(connectorId, position: 0, ct);
	}

	[Test]
	public async ValueTask starts_connector_from_log_position(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Start(connectorId, position: 0, ct);
	}

	[Test]
	public async ValueTask stops_running_connector(CancellationToken ct) {
		var connectorId = KurrentDBFixture.NewConnectorId();
		var settings    = KurrentDBFixture.NewConnectorSettings();

		await Fixture.Client.Connectors
			.Create(connectorId, settings, ct);

		await Fixture.Client.Connectors
			.Start(connectorId, ct);

		await Fixture.Client.Connectors
			.Stop(connectorId, ct);
	}
}
