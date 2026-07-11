namespace Kurrent.Surge.Mcp;

/// <summary>
/// The version-neutral persisted shape of a task. Scalar columns plus opaque JSON payloads —
/// each adapter serializes its own package-specific types (JsonRpcRequest / RequestId for 1.4.1,
/// InputRequests for 2.0, result / error for both) into these string columns, keeping the Core
/// engine free of any ModelContextProtocol dependency.
/// </summary>
/// <remarks>
/// Timestamps are always UTC. <see cref="ExpiresAt"/> is derived (<see cref="CreatedAt"/> +
/// time-to-live) and persisted so expiry can be filtered and swept with a single indexed column;
/// a null value means unlimited lifetime.
/// </remarks>
[PublicAPI]
public sealed record TaskRecord {
    /// <summary>Unique, monotonically-increasing (UUIDv7) task identifier. Doubles as the list cursor.</summary>
    public required string TaskId { get; init; }

    /// <summary>Owning session, or <see langword="null"/> for the anonymous/global bucket.</summary>
    public string? SessionId { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required McpTaskState Status { get; init; }

    /// <summary>Optional human-readable message describing the current state.</summary>
    public string? StatusMessage { get; init; }

    /// <summary>When the task was created (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the task was last mutated (UTC).</summary>
    public required DateTimeOffset LastUpdatedAt { get; init; }

    /// <summary>Retention duration from creation, or <see langword="null"/> for unlimited.</summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Absolute expiry instant (<see cref="CreatedAt"/> + <see cref="TimeToLive"/>), or null for unlimited.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Suggested polling interval, or <see langword="null"/> if not advertised.</summary>
    public TimeSpan? PollInterval { get; init; }

    /// <summary>Serialized originating request (1.4.1 <c>JsonRpcRequest</c>), or null.</summary>
    public string? RequestJson { get; init; }

    /// <summary>Serialized originating request id (1.4.1 <c>RequestId</c>), or null.</summary>
    public string? RequestIdJson { get; init; }

    /// <summary>Serialized success result payload, or null if none stored.</summary>
    public string? ResultJson { get; init; }

    /// <summary>Serialized error payload (2.0 distinguishes error from result), or null.</summary>
    public string? ErrorJson { get; init; }

    /// <summary>Serialized pending input-requests map (2.0), or null.</summary>
    public string? InputRequestsJson { get; init; }
}
