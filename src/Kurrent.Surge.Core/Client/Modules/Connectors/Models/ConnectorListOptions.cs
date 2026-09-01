namespace Kurrent.Surge.Client.Connectors;

/// <summary>
/// Options for listing connectors.
/// </summary>
public sealed class ConnectorListOptions {
    /// <summary>
    /// Filter connectors by their operational state.
    /// </summary>
    public IReadOnlyList<ConnectorState> States { get; set; } = [];

    /// <summary>
    /// Filter connectors by instance type names.
    /// </summary>
    public IReadOnlyList<string> InstanceTypeNames { get; set; } = [];

    /// <summary>
    /// Filter connectors by specific connector IDs.
    /// </summary>
    public IReadOnlyList<ConnectorId> ConnectorIds { get; set; } = [];

    /// <summary>
    /// Include connector settings in the response.
    /// </summary>
    public bool IncludeSettings { get; set; }

    /// <summary>
    /// Include hierarchical configuration in the response.
    /// </summary>
    public bool IncludeConfiguration { get; set; }

    /// <summary>
    /// Include deleted connectors in the response.
    /// </summary>
    public bool ShowDeleted { get; set; }

    /// <summary>
    /// Page number for pagination (1-based).
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Number of items per page for pagination.
    /// </summary>
    public int PageSize { get; set; } = 50;
}
