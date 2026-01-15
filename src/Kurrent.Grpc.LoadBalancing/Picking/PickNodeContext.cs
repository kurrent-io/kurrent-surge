namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Context provided to the picking strategy.
/// </summary>
/// <param name="Nodes">Available nodes with their subchannels.</param>
/// <param name="Request">The HTTP request being routed (may be null in some edge cases).</param>
public readonly record struct PickNodeContext(SubchannelNode[] Nodes, HttpRequestMessage? Request);
