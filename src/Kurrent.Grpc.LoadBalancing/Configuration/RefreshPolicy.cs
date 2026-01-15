using Grpc.Core;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Determines whether a gRPC exception should trigger topology refresh.
/// </summary>
/// <param name="exception">The exception that occurred.</param>
/// <returns>True if topology should be refreshed.</returns>
public delegate bool ShouldRefreshTopology(RpcException exception);

/// <summary>
/// Factory methods for creating refresh policies.
/// </summary>
public static class RefreshPolicy {
    /// <summary>
    /// Default policy: refresh on StatusCode.Unavailable.
    /// </summary>
    public static readonly ShouldRefreshTopology Default =
        static ex => ex.StatusCode == StatusCode.Unavailable;

    /// <summary>
    /// Never refresh on any error.
    /// </summary>
    public static readonly ShouldRefreshTopology Never =
        static _ => false;

    /// <summary>
    /// Always refresh on any error.
    /// </summary>
    public static readonly ShouldRefreshTopology Always =
        static _ => true;

    /// <summary>
    /// Refresh on specific status codes.
    /// </summary>
    /// <param name="codes">Status codes that trigger refresh.</param>
    public static ShouldRefreshTopology OnStatusCodes(params StatusCode[] codes) {
        var set = codes.ToHashSet();
        return ex => set.Contains(ex.StatusCode);
    }

    /// <summary>
    /// Refresh when exception message contains any of the triggers.
    /// </summary>
    /// <param name="triggers">Substrings to match (case-insensitive).</param>
    public static ShouldRefreshTopology OnMessageContains(params string[] triggers) =>
        ex => triggers.Any(t => ex.Message.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refresh when exception trailers contain specific key.
    /// </summary>
    /// <param name="key">Trailer key to check.</param>
    public static ShouldRefreshTopology OnTrailerKey(string key) =>
        ex => ex.Trailers.Any(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Refresh if ANY of the policies returns true.
    /// </summary>
    public static ShouldRefreshTopology Any(params ShouldRefreshTopology[] policies) =>
        ex => policies.Any(p => p(ex));

    /// <summary>
    /// Refresh if ALL policies return true.
    /// </summary>
    public static ShouldRefreshTopology All(params ShouldRefreshTopology[] policies) =>
        ex => policies.All(p => p(ex));

    /// <summary>
    /// Creates policy from integer status codes (for config binding).
    /// </summary>
    /// <param name="statusCodes">Integer status codes.</param>
    public static ShouldRefreshTopology FromStatusCodeInts(params int[] statusCodes) =>
        OnStatusCodes(statusCodes.Select(c => (StatusCode)c).ToArray());
}
