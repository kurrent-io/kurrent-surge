namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Immutable snapshot of cluster topology at a point in time.
/// </summary>
/// <remarks>
/// This is a value object with cached hash code for efficient comparison.
/// Topology changes trigger picker recreation in the load balancer.
/// </remarks>
public sealed record ClusterTopology {
    /// <summary>All known nodes in the cluster.</summary>
    public required IReadOnlyList<ClusterNode> Nodes { get; init; }

    /// <summary>When this topology snapshot was captured.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    // Cached hash for efficient equality checks (0 = not computed)
    int _cachedHash;

    /// <summary>Returns nodes filtered by eligibility.</summary>
    public IEnumerable<ClusterNode> GetEligibleNodes() =>
        Nodes.Where(n => n.IsEligible);

    /// <summary>Returns nodes that can serve reads.</summary>
    public IEnumerable<ClusterNode> GetReadableNodes() =>
        Nodes.Where(n => n.CanServeReads);

    /// <summary>Returns nodes that can accept writes.</summary>
    public IEnumerable<ClusterNode> GetWritableNodes() =>
        Nodes.Where(n => n.CanAcceptWrites);

    /// <summary>
    /// Thread-safe cached hash code computation.
    /// </summary>
    public override int GetHashCode() {
        var cached = Volatile.Read(ref _cachedHash);
        if (cached != 0)
            return cached;

        var hash = new HashCode();
        foreach (var node in Nodes)
            hash.Add(node);

        var computed = hash.ToHashCode();
        // Avoid 0 as it means "not computed"
        if (computed == 0)
            computed = 1;

        Interlocked.CompareExchange(ref _cachedHash, computed, 0);
        return Volatile.Read(ref _cachedHash);
    }
}
