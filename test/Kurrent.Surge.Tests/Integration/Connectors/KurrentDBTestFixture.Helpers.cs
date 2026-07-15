// ReSharper disable CheckNamespace

using Microsoft.Extensions.Configuration;

namespace Kurrent.Surge.Tests.Fixtures;

public static class KurrentDBTestFixtureConnectorExtensions {
	extension(KurrentDBFixture) {
        public static string NewConnectorId() => $"test-connector-{Guid.NewGuid():N}";

        public static string NewConnectorName() => $"Test Connector {Guid.NewGuid():N}";

        public static Dictionary<string, string> NewConnectorSettings() => new() {
            ["instanceTypeName"]               = "serilog-sink",
            ["subscription:filter:scope"]      = "stream",
            ["subscription:filter:filterType"] = "prefix",
            ["subscription:filter:expression"] = "test-",
            ["subscription:initialPosition"]   = "latest"
        };

        public static IConfiguration NewConnectorConfiguration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(KurrentDBFixture.NewConnectorSettings()
                    .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
                .Build();
    }
}
