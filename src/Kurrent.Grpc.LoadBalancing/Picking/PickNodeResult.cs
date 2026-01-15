using Grpc.Core;
using Grpc.Net.Client.Balancer;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Result of a node picking operation.
/// Wraps gRPC's PickResult with domain-specific factory methods.
/// </summary>
public readonly struct PickNodeResult {
    readonly Subchannel? _subchannel;
    readonly Status? _status;
    readonly PickNodeResultType _type;

    enum PickNodeResultType { Subchannel, Fail, Drop, Defer }

    // === Success ===

    /// <summary>Route to the specified subchannel.</summary>
    public static PickNodeResult ForSubchannel(Subchannel subchannel) =>
        new(PickNodeResultType.Subchannel, subchannel, null);

    /// <summary>Route to the specified node's subchannel.</summary>
    public static PickNodeResult ForNode(SubchannelNode node) =>
        ForSubchannel(node.Subchannel);

    // === Defer ===

    /// <summary>Wait for a new picker (topology refresh in progress).</summary>
    public static PickNodeResult Defer() =>
        new(PickNodeResultType.Defer, null, null);

    // === Drop ===

    /// <summary>Fail immediately, bypass retry policy.</summary>
    public static PickNodeResult Drop(string reason) =>
        new(PickNodeResultType.Drop, null, new Status(StatusCode.Unavailable, reason));

    // === Domain-Specific Failures ===

    /// <summary>No leader available for write operations.</summary>
    public static PickNodeResult NoLeader() =>
        Fail(StatusCode.Unavailable, "No leader available for write operations");

    /// <summary>No followers available for read operations.</summary>
    public static PickNodeResult NoFollowers() =>
        Fail(StatusCode.Unavailable, "No followers available for read operations");

    /// <summary>No eligible nodes in cluster.</summary>
    public static PickNodeResult NoEligibleNodes() =>
        Fail(StatusCode.Unavailable, "No eligible nodes in cluster");

    /// <summary>Cluster is not ready.</summary>
    public static PickNodeResult ClusterNotReady() =>
        Fail(StatusCode.Unavailable, "Cluster is not ready");

    // === Generic Failures ===

    /// <summary>Fail with specified status code and message.</summary>
    public static PickNodeResult Fail(StatusCode code, string message) =>
        new(PickNodeResultType.Fail, null, new Status(code, message));

    /// <summary>Fail with Unavailable status and specified message.</summary>
    public static PickNodeResult Fail(string message) =>
        Fail(StatusCode.Unavailable, message);

    // === Internal ===

    /// <summary>Convert to gRPC's PickResult.</summary>
    internal PickResult ToPickResult() => _type switch {
        PickNodeResultType.Subchannel => PickResult.ForSubchannel(_subchannel!),
        PickNodeResultType.Defer      => PickResult.ForQueue(),
        PickNodeResultType.Drop       => PickResult.ForDrop(_status!.Value),
        PickNodeResultType.Fail       => PickResult.ForFailure(_status!.Value),
        _                             => PickResult.ForFailure(new Status(StatusCode.Internal, "Invalid pick result"))
    };

    PickNodeResult(PickNodeResultType type, Subchannel? subchannel, Status? status) {
        _type = type;
        _subchannel = subchannel;
        _status = status;
    }
}
