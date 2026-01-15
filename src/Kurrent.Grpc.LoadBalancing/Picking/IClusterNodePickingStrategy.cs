namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Strategy for selecting which node to route a request to.
/// </summary>
public interface IClusterNodePickingStrategy {
    /// <summary>
    /// Pick a node for the given request context.
    /// </summary>
    /// <param name="context">The picking context with available nodes and request.</param>
    /// <returns>The pick result indicating which node to use or why picking failed.</returns>
    PickNodeResult Pick(in PickNodeContext context);
}
