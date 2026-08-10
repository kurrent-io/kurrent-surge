using System.Net;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// The identity of a cluster node: host and port with value equality. The host is
/// normalized to lowercase at construction, so the same node is never tracked twice
/// under different casings.
/// </summary>
readonly record struct ClusterEndpoint {
    public ClusterEndpoint(string host, int port) {
        // ToLowerInvariant always returns a new string, so skip it for the common case of an
        // already-lowercase ASCII hostname; non-ASCII hosts take the full conversion.
        Host = System.Text.Ascii.IsValid(host) && !host.AsSpan().ContainsAnyInRange('A', 'Z')
            ? host
            : host.ToLowerInvariant();
        Port = port;
    }

    public string Host { get; }
    public int    Port { get; }

    public static ClusterEndpoint From(DnsEndPoint endpoint) => new(endpoint.Host, endpoint.Port);

    public static implicit operator DnsEndPoint(ClusterEndpoint endpoint) => new(endpoint.Host, endpoint.Port);

    public override string ToString() => $"{Host}:{Port}";
}
