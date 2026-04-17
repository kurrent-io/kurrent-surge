using System.Net;

namespace Kurrent.Client.Errors;

/// <summary>
/// Detailed information about a Kurrent error, including reason, metadata, retry info, resource info, and auth info.
/// <remarks>
/// This class is used to encapsulate structured error details that can be included in exceptions.
/// It provides a way to centralize and convey rich error information.
/// </remarks>
/// </summary>
[PublicAPI]
public class KurrentErrorDetails {
    public static readonly KurrentErrorDetails None = new();

    public ErrorCode    ErrorCode    { get; set; } = ErrorCode.Unknown;
    public Metadata     Metadata     { get; set; } = Metadata.New;
    public RetryInfo    RetryInfo    { get; set; } = RetryInfo.None;
    public ResourceInfo ResourceInfo { get; set; } = ResourceInfo.None;

    public bool HasMetadata     => Metadata.Count > 0;
    public bool HasRetryInfo    => RetryInfo != RetryInfo.None;
    public bool HasResourceInfo => ResourceInfo != ResourceInfo.None;
}

/// <summary>
/// Information about a specific resource within the system.
/// </summary>
[PublicAPI]
public record ResourceInfo {
    public static readonly ResourceInfo None = new("", "");

    ResourceInfo(string type, string id) {
        Type = type;
        Id   = id;
    }

    /// <summary>
    /// The type of the resource (e.g., "Stream", "Registry", etc.).
    /// </summary>
    public string Type { get; internal init; }

    /// <summary>
    /// The unique identifier of the resource.
    /// </summary>
    public string Id { get; internal init; }

    public static ResourceInfo From(string type, string id) {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new(type, id);
    }

    public override string ToString() => this != None ? $"{Type}('{Id}')" : "";
}

/// <summary>
/// Information about the current leader node in a clustered deployment.
/// </summary>
[PublicAPI]
public record NodeInfo {
    public static readonly NodeInfo None = new(null!, Guid.Empty);

    NodeInfo(DnsEndPoint endPoint, Guid nodeId) {
        EndPoint = endPoint;
        NodeId   = nodeId;
    }

    /// <summary>
    /// The network endpoint (host and port) of the current leader node.
    /// </summary>
    public DnsEndPoint EndPoint { get; internal init; }

    /// <summary>
    /// The unique instance ID of the current leader node.
    /// </summary>
    public Guid NodeId { get; internal init; }

    public static NodeInfo From(string host, int port, string nodeId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        if (!Guid.TryParse(nodeId, out var id))
            throw new ArgumentException("NodeId must be a valid GUID.", nameof(nodeId));

        return new(new(host, port), id);
    }

    public static NodeInfo From(DnsEndPoint endpoint, Guid nodeId) => new(endpoint, nodeId);

    public override string ToString() =>
        this != None ? $"{NodeId} @ {EndPoint}" : "";
}

/// <summary>
/// Information about whether an operation can be retried, and if so, after what delay.
/// </summary>
[PublicAPI]
public readonly record struct RetryInfo {
    public static readonly RetryInfo None = new(TimeSpan.MinValue);

    RetryInfo(TimeSpan delay) {
        Delay     = delay;
        Retryable = delay >= TimeSpan.Zero;
    }

    public TimeSpan Delay     { get; internal init; }
    public bool     Retryable { get; internal init; }

    public static RetryInfo From(TimeSpan delay) =>
        delay < TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(delay), "Retry Delay must be non-negative ")
            : new RetryInfo(delay);

    public override string ToString() =>
        Retryable
            ? Delay == TimeSpan.Zero
                ? "Retry immediately"
                : $"Retry after {Delay}"
            : "Do not retry";
}
