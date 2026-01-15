namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Round-robin across all eligible nodes.
/// No read/write distinction.
/// </summary>
/// <remarks>
/// Zero-allocation: Uses simple loop with modular indexing.
/// </remarks>
public sealed class RoundRobinStrategy : IClusterNodePickingStrategy {
    /// <summary>Singleton instance.</summary>
    public static RoundRobinStrategy Instance { get; } = new();

    long _index;

    /// <inheritdoc/>
    public PickNodeResult Pick(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Count eligible nodes (single pass)
        var eligibleCount = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.IsEligible)
                eligibleCount++;
        }

        if (eligibleCount == 0)
            return PickNodeResult.NoEligibleNodes();

        // Find the Nth eligible node
        var targetIndex = (int)(Interlocked.Increment(ref _index) % eligibleCount);
        var count = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.IsEligible) {
                if (count == targetIndex)
                    return PickNodeResult.ForNode(nodes[i]);

                count++;
            }
        }

        return PickNodeResult.NoEligibleNodes(); // Shouldn't happen
    }
}
