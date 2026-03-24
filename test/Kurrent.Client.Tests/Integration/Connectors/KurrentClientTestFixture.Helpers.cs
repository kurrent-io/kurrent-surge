// ReSharper disable CheckNamespace

using Kurrent.Client.Connectors;

namespace Kurrent.Client.Tests.Fixtures;

public partial class KurrentClientTestFixture {
	public static string NewConnectorId() => $"test-connector-{Guid.NewGuid():N}";

	public static string NewConnectorName() => $"Test Connector {Guid.NewGuid():N}";

	public static Dictionary<string, string?> NewConnectorSettings() => new() {
		["instanceTypeName"] = "serilog-sink",
		["subscription:filter:scope"] = "stream",
		["subscription:filter:filterType"] = "prefix",
		["subscription:filter:expression"] = "test-",
		["subscription:initialPosition"] = "latest"
	};

	public static Google.Protobuf.WellKnownTypes.Struct NewConnectorConfiguration() =>
		NewConnectorSettings().ToProtobufStruct();
}
