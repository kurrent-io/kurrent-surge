// ReSharper disable CheckNamespace

using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Connectors;

namespace Kurrent.Client.Tests.Fixtures;

public static class KurrentDBTestFixtureConnectorExtensions {
	extension(KurrentDBFixture) {
        public static string NewConnectorId() => $"test-connector-{Guid.NewGuid():N}";

        public static string NewConnectorName() => $"Test Connector {Guid.NewGuid():N}";

        public static Dictionary<string, string?> NewConnectorSettings() => new() {
            ["instanceTypeName"]               = "serilog-sink",
            ["subscription:filter:scope"]      = "stream",
            ["subscription:filter:filterType"] = "prefix",
            ["subscription:filter:expression"] = "test-",
            ["subscription:initialPosition"]   = "latest"
        };

        public static Struct NewConnectorConfiguration() =>
            KurrentDBFixture.NewConnectorSettings().ToProtobufStruct();
    }
}
