namespace Kurrent.Surge.Errors;

/// <summary>
/// Errors raised by the server itself rather than by a module. All four are retriable, which is why
/// they expose their <see cref="ErrorCode"/> — the processor resilience predicate keys off it.
/// </summary>
[PublicAPI]
public class LeaderForwardingTimeoutException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("LEADER_FORWARDING_TIMEOUT");

    public LeaderForwardingTimeoutException(string message) : base(Code, message) { }

    public LeaderForwardingTimeoutException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ServerNotReadyException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("SERVER_NOT_READY");

    public ServerNotReadyException(string message) : base(Code, message) { }

    public ServerNotReadyException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class ServerOverloadedException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("SERVER_OVERLOADED");

    public ServerOverloadedException(string message) : base(Code, message) { }

    public ServerOverloadedException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }
}

[PublicAPI]
public class NotLeaderNodeException : KurrentException {
    public static ErrorCode Code { get; } = ErrorCode.From("NOT_LEADER_NODE");

    public NotLeaderNodeException(string message) : base(Code, message) { }

    public NotLeaderNodeException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(details, reason, innerException) { }

    /// <summary>
    /// The node to redirect to, when the server sent one.
    /// </summary>
    /// <remarks>
    /// Best-effort: the hint is optional on the wire and may be absent entirely. A missing hint must not
    /// turn a retriable not-leader error into a crash — the exception still carries the error code the
    /// retry predicate keys off.
    /// </remarks>
    public NodeInfo CurrentLeader { get; init; } = NodeInfo.None;
}
