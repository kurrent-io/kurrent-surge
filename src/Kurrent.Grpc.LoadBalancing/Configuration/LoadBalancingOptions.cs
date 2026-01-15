namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Configuration options for cluster load balancing.
/// JSON-serializable for IConfiguration binding.
/// </summary>
/// <example>
/// <code>
/// {
///   "LoadBalancing": {
///     "Seeds": ["node1:2113", "node2:2113"],
///     "UseTls": true,
///     "Resilience": {
///       "Timeout": "00:00:05",
///       "MaxDiscoveryAttempts": 10
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class LoadBalancingOptions {
    /// <summary>
    /// Seed endpoints for initial cluster discovery.
    /// Format: "host:port" or "host" (uses default port).
    /// </summary>
    public string[] Seeds { get; set; } = [];

    /// <summary>
    /// Whether to use TLS for connections.
    /// </summary>
    public bool UseTls { get; set; }

    /// <summary>
    /// Resilience and retry settings.
    /// </summary>
    public ResilienceOptions Resilience { get; set; } = new();
}
