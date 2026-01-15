namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Routes all requests to the leader.
/// Use for strict consistency requirements.
/// </summary>
/// <remarks>
/// Zero-allocation: Uses simple loop scan.
/// </remarks>
public sealed class LeaderOnlyStrategy : IClusterNodePickingStrategy {
    /// <summary>Singleton instance.</summary>
    public static LeaderOnlyStrategy Instance { get; } = new();

    /// <inheritdoc/>
    public PickNodeResult Pick(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Simple loop - no LINQ allocation
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanAcceptWrites)
                return PickNodeResult.ForNode(nodes[i]);
        }

        return PickNodeResult.NoLeader();
    }
}
