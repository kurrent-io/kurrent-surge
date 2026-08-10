using System.Net;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// The identity of a cluster node: host and port with value equality. The host is
/// normalized to lowercase at construction, so the same node is never tracked twice
/// under different casings.
/// </summary>
readonly record struct ClusterEndpoint {
    public ClusterEndpoint(string host, int port) {
        Host = host.ToLowerInvariant();
        Port = port;
    }

    public string Host { get; }
    public int    Port { get; }

    public static ClusterEndpoint From(DnsEndPoint endpoint) => new(endpoint.Host, endpoint.Port);

    public static implicit operator DnsEndPoint(ClusterEndpoint endpoint) => new(endpoint.Host, endpoint.Port);

    public override string ToString() => $"{Host}:{Port}";
}
