namespace Kurrent.Client.Streams;

/// <summary>
/// Defines the interface for processing records from a stream read operation.
/// Implement this interface to receive callbacks during the read lifecycle.
/// <para>
/// All methods except <see cref="ProcessRecords"/> have default implementations
/// that do nothing, allowing you to override only the methods you need.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Minimal implementation - only ProcessRecords is required
/// class MinimalProcessor : IRecordProcessor {
///     public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
///         foreach (var record in ctx.Records)
///             Console.WriteLine(record.EventType);
///         return ValueTask.CompletedTask;
///     }
/// }
///
/// // Full implementation with shutdown handling
/// class FullProcessor : IRecordProcessor {
///     public ValueTask Initialize(InitializationContext ctx) {
///         Console.WriteLine($"Starting: {ctx.ProcessId}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
///         // Process records...
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask OnShutdown(ShutdownContext ctx) {
///         if (ctx.Reason == ShutdownReason.Error)
///             Console.WriteLine($"Error: {ctx.Error}");
///         else
///             Console.WriteLine($"Done: {ctx.TotalRecords} records");
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
[PublicAPI]
public interface IRecordProcessor {
    /// <summary>
    /// Called when the read operation starts, before any records are processed.
    /// </summary>
    /// <param name="context">
    /// Context containing the process ID, read options, and cancellation token.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask Initialize(InitializationContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when records are available for processing.
    /// <remarks>
    /// This is the only method that must be implemented.
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the batch of records, lag information, and cancellation token.
    /// </param>
    ValueTask ProcessRecords(ProcessRecordsContext context);

    /// <summary>
    /// Called when a heartbeat is received.
    /// Heartbeats provide position updates and indicate the stream is alive.
    /// </summary>
    /// <param name="context">
    /// Context containing the heartbeat information and cancellation token.
    /// </param>
    ValueTask OnHeartbeat(HeartbeatContext context) => ValueTask.CompletedTask;

    /// <summary>
    /// Called when processing stops, regardless of reason.
    /// <remarks>
    /// <para>
    /// This method is invoked at the end of the processing lifecycle, allowing for cleanup
    /// </para>
    /// <para>
    /// Check <see cref="ShutdownContext.Reason"/> to determine why processing stopped.
    /// </para>
    /// </remarks>
    /// </summary>
    /// <param name="context">
    /// Context containing the shutdown reason, final position, total records processed,
    /// duration, error (if any), and cancellation token.
    /// </param>
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}

public enum ShutdownReason {
    /// <summary>
    /// Processing completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Processing was cancelled either by user request or server-side timeout.
    /// </summary>
    Cancelled,

    /// <summary>
    /// An error occurred during processing.
    /// </summary>
    Error
}
