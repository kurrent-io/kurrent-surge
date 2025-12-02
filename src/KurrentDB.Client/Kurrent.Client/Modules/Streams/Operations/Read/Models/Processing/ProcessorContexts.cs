namespace Kurrent.Client.Streams;

/// <summary>
/// Context provided when a record processor is initialized.
/// </summary>
/// <param name="Options">The processing options used for this operation.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record InitializationContext(
    ProcessOptions Options,
    CancellationToken CancellationToken
);

/// <summary>
/// Context provided when records are available for processing.
/// </summary>
/// <param name="SubscriptionId">Unique identifier of the durable subscription.</param>
/// <param name="Records">The batch of records to process.</param>
/// <param name="Lag">How far behind the head of stream, or null if unknown.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record ProcessRecordsContext(
    Guid SubscriptionId,
    IReadOnlyList<Record> Records,
    TimeSpan? Lag,
    CancellationToken CancellationToken
) {
    // TODO SS: consider using a cancellation token source instead of throwing directly
    // quick and easy way to stop processing from within the processor
    public void StopProcessing() => throw new OperationCanceledException("Processing stopped by user request.");
}

/// <summary>
/// Context provided when a heartbeat is received while processing.
/// </summary>
/// <param name="SubscriptionId">Unique identifier of the durable subscription.</param>
/// <param name="Heartbeat">The heartbeat containing position and timing information.</param>
/// <param name="CancellationToken">Token to observe for cancellation requests.</param>
[PublicAPI]
public record HeartbeatContext(
    Guid SubscriptionId,
    Heartbeat Heartbeat,
    CancellationToken CancellationToken
);

/// <summary>
/// Context provided when processing stops, regardless of reason.
/// </summary>
/// <param name="SubscriptionId">Unique identifier of the durable subscription.</param>
/// <param name="Reason">Why processing stopped (Completed, Cancelled, Error).</param>
/// <param name="FinalPosition">The final log position reached.</param>
/// <param name="TotalRecords">Total number of records processed.</param>
/// <param name="Duration">Total time elapsed during processing.</param>
/// <param name="Error">The error that caused shutdown, or null if not an error.</param>
[PublicAPI]
public record ShutdownContext(
    ShutdownReason Reason,
    Guid SubscriptionId,
    LogPosition FinalPosition,
    long TotalRecords,
    TimeSpan Duration,
    ProcessError? Error = null
);
