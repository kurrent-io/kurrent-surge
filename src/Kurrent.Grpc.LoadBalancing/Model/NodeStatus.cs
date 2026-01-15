namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Represents the node's synchronization HEALTH and operational progress.
/// This is the "how" axis - how the node is performing.
/// </summary>
public enum NodeStatus {
    /// <summary>Booting or discovering peers.</summary>
    Starting = 0,

    /// <summary>Catching up on logs (replicating data).</summary>
    Syncing = 1,

    /// <summary>Fully operational and current.</summary>
    Ready = 2,

    /// <summary>Healthy but lost quorum contact (can serve stale reads).</summary>
    Isolated = 3,

    /// <summary>Graceful shutdown in progress.</summary>
    Stopping = 4,

    /// <summary>Shutdown complete.</summary>
    Stopped = 5
}
