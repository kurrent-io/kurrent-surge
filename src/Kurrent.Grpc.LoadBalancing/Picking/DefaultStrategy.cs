namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Routes writes to leader, reads to followers with round-robin.
/// Falls back to leader for reads if no followers available.
/// </summary>
/// <remarks>
/// <para>
/// Zero-allocation: Uses simple loops with role priority scanning.
/// </para>
/// <para>
/// Read preference order: ReadOnly → Follower → Leader
/// </para>
/// </remarks>
public sealed class DefaultStrategy : IClusterNodePickingStrategy {
    /// <summary>Singleton instance with default write method prefixes.</summary>
    public static DefaultStrategy Instance { get; } = new();

    long _readOnlyIndex;
    long _followerIndex;
    long _leaderIndex;
    readonly string[] _writeMethodPrefixes;

    /// <summary>
    /// Initializes a new default strategy with standard write method prefixes.
    /// </summary>
    public DefaultStrategy() : this("Append", "Delete", "Create", "Set", "Update", "Write", "Put", "Remove") { }

    /// <summary>
    /// Initializes a new default strategy with custom write method prefixes.
    /// </summary>
    /// <param name="writeMethodPrefixes">Prefixes that identify write methods.</param>
    public DefaultStrategy(params string[] writeMethodPrefixes) {
        _writeMethodPrefixes = writeMethodPrefixes;
    }

    /// <inheritdoc/>
    public PickNodeResult Pick(in PickNodeContext context) {
        if (context.Nodes.Length == 0)
            return PickNodeResult.NoEligibleNodes();

        var method = ExtractMethodName(context.Request);
        var isWrite = IsWriteMethod(method);

        return isWrite ? PickForWrite(context) : PickForRead(context);
    }

    PickNodeResult PickForWrite(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Simple loop - no LINQ allocation
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanAcceptWrites)
                return PickNodeResult.ForNode(nodes[i]);
        }

        return PickNodeResult.NoLeader();
    }

    PickNodeResult PickForRead(in PickNodeContext context) {
        var nodes = context.Nodes;

        // Count nodes by role (single pass)
        int readOnlyCount = 0, followerCount = 0, leaderCount = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (!nodes[i].Node.CanServeReads)
                continue;

            switch (nodes[i].Node.Role) {
                case NodeRole.ReadOnly: readOnlyCount++; break;
                case NodeRole.Follower: followerCount++; break;
                case NodeRole.Leader:   leaderCount++;   break;
            }
        }

        // Prefer ReadOnly → Follower → Leader
        if (readOnlyCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _readOnlyIndex) % readOnlyCount);
            return FindNthByRole(nodes, NodeRole.ReadOnly, targetIndex);
        }

        if (followerCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _followerIndex) % followerCount);
            return FindNthByRole(nodes, NodeRole.Follower, targetIndex);
        }

        if (leaderCount > 0) {
            var targetIndex = (int)(Interlocked.Increment(ref _leaderIndex) % leaderCount);
            return FindNthByRole(nodes, NodeRole.Leader, targetIndex);
        }

        return PickNodeResult.NoEligibleNodes();
    }

    static PickNodeResult FindNthByRole(SubchannelNode[] nodes, NodeRole role, int targetIndex) {
        var count = 0;
        for (var i = 0; i < nodes.Length; i++) {
            if (nodes[i].Node.CanServeReads && nodes[i].Node.Role == role) {
                if (count == targetIndex)
                    return PickNodeResult.ForNode(nodes[i]);

                count++;
            }
        }

        return PickNodeResult.NoEligibleNodes(); // Shouldn't happen
    }

    bool IsWriteMethod(string method) {
        // Simple loop - no LINQ allocation
        for (var i = 0; i < _writeMethodPrefixes.Length; i++) {
            if (method.StartsWith(_writeMethodPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static string ExtractMethodName(HttpRequestMessage? request) =>
        request?.RequestUri?.Segments.LastOrDefault() ?? "";
}
