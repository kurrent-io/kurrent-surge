namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Resilience settings for discovery and connection management.
/// JSON-serializable for IConfiguration binding.
/// </summary>
public sealed class ResilienceOptions {
    /// <summary>
    /// Timeout for individual discovery calls.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum discovery attempts before giving up.
    /// Default: 10.
    /// </summary>
    public int MaxDiscoveryAttempts { get; set; } = 10;

    /// <summary>
    /// Initial backoff delay between failed discovery rounds.
    /// Default: 100ms.
    /// </summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum backoff delay.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// gRPC status codes that trigger topology refresh.
    /// Default: [14] (Unavailable).
    /// </summary>
    /// <remarks>
    /// When no custom RefreshPolicy is set, this array is converted to
    /// a RefreshPolicy via <see cref="RefreshPolicy.FromStatusCodeInts"/>.
    /// </remarks>
    public int[] RefreshOnStatusCodes { get; set; } = [14]; // StatusCode.Unavailable
}
