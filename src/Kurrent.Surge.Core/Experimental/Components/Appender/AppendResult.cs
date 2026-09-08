using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Outcome of an appended record. Wraps the server response on success, or
/// carries error/drop information on failure.
/// </summary>
public sealed record AppendResult {
    AppendResult(AppendResultStatus status) => Status = status;

    internal AppendResult(AppendRecordsResponse response) {
        Status   = AppendResultStatus.Acknowledged;
        Response = response;
    }

    /// <summary>
    /// The outcome status of the append operation.
    /// </summary>
    public AppendResultStatus Status { get; }

    /// <summary>
    /// The server response. Only set when <see cref="Status"/> is
    /// <see cref="AppendResultStatus.Acknowledged"/>.
    /// </summary>
    public AppendRecordsResponse? Response { get; }

    /// <summary>
    /// The exception that caused the failure. Only set when <see cref="Status"/> is
    /// <see cref="AppendResultStatus.Failed"/>.
    /// </summary>
    public Exception? Error { get; private init; }

    public bool IsSuccess => Status is AppendResultStatus.Acknowledged;

    /// <summary>
    /// Creates a result indicating the record was dropped before reaching the server.
    /// Reserved for drop-mode support; no current path produces it — a full buffer
    /// applies backpressure instead of dropping.
    /// </summary>
    internal static AppendResult Dropped() => new(AppendResultStatus.Dropped);

    /// <summary>
    /// Creates a result indicating the batch containing this record failed.
    /// </summary>
    internal static AppendResult Failed(Exception error) =>
        new(AppendResultStatus.Failed) { Error = error };
}

/// <summary>
/// Status of an individual append operation.
/// </summary>
public enum AppendResultStatus {
    /// <summary>
    /// The record's batch was acknowledged by the server.
    /// </summary>
    Acknowledged,

    /// <summary>
    /// The batch containing this record failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The record was dropped before reaching the server. Reserved for drop-mode support;
    /// <see cref="AppenderOptions.FullMode"/> only permits backpressure today.
    /// </summary>
    Dropped
}

/// <summary>
/// Error information passed to <see cref="AppenderOptions.OnError"/>.
/// </summary>
public sealed record AppendError(Exception Exception);
