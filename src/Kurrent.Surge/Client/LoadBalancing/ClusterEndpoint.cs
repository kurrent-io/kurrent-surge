using System.Buffers;
using System.Net;

namespace Kurrent.Surge.Client.LoadBalancing;

/// <summary>
/// The identity of a cluster node: host and port with value equality. The host is
/// normalized to lowercase at construction, so the same node is never tracked twice
/// under different casings.
/// </summary>
readonly record struct ClusterEndpoint {
    static readonly SearchValues<char> LowercaseSafeAscii = CreateLowercaseSafeAscii();

    public ClusterEndpoint(string host, int port) { 
        Host = host.AsSpan().ContainsAnyExcept(LowercaseSafeAscii) ? host.ToLowerInvariant() : host;
        Port = port;
    }

    public string Host { get; }
    public int    Port { get; }

    public static ClusterEndpoint From(DnsEndPoint endpoint) => new(endpoint.Host, endpoint.Port);

    public static implicit operator DnsEndPoint(ClusterEndpoint endpoint) => new(endpoint.Host, endpoint.Port);

    public override string ToString() => $"{Host}:{Port}";

    static SearchValues<char> CreateLowercaseSafeAscii() {
        // All of ASCII except 'A'-'Z': a host made only of these characters is already in
        // canonical form. Anything outside the set — ASCII uppercase or any non-ASCII
        // character — takes the full conversion.
        
        Span<char> chars = stackalloc char[102];
        
        var count = 0;
        for (var c = '\0'; c <= '\x7F'; c++)
            if (c is < 'A' or > 'Z')
                chars[count++] = c;

        return SearchValues.Create(chars);
    }
}
