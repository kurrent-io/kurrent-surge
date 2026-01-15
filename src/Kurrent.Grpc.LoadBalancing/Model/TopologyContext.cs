using System.Net;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// Context provided to topology sources for discovery operations.
/// </summary>
/// <param name="Channel">gRPC channel for making discovery calls.</param>
/// <param name="Seeds">Seed endpoints for initial discovery.</param>
/// <param name="CancellationToken">Token for cancellation.</param>
public sealed record TopologyContext(
    ChannelBase Channel,
    IReadOnlyList<DnsEndPoint> Seeds,
    CancellationToken CancellationToken = default
) {
    /// <summary>Optional logger for discovery operations.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>Optional timeout for discovery calls.</summary>
    public TimeSpan? Timeout { get; init; }
}
