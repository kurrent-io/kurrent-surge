using System.Net;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Compares endpoints by host (case-insensitive) and port, so the same node is never
/// tracked twice under different host casings.
/// </summary>
sealed class DnsEndPointComparer : IEqualityComparer<DnsEndPoint> {
    public static readonly DnsEndPointComparer Instance = new();

    public bool Equals(DnsEndPoint? x, DnsEndPoint? y) {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;

        return string.Equals(x.Host, y.Host, StringComparison.OrdinalIgnoreCase)
            && x.Port == y.Port;
    }

    public int GetHashCode(DnsEndPoint obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Host), obj.Port);
}
