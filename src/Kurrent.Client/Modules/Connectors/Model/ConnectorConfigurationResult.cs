using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Client.Connectors;

/// <summary>
/// Connector configuration with metadata.
/// </summary>
public record ConnectorConfigurationResult {
    /// <summary>
    /// Hierarchical configuration for the connector.
    /// </summary>
    public required Struct Configuration { get; init; }

    /// <summary>
    /// Timestamp when the configuration was last updated.
    /// </summary>
    public DateTimeOffset ConfigurationUpdateTime { get; init; }
}
