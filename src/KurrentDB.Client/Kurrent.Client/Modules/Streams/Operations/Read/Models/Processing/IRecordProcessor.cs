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


/// <summary>
/// Defines the interface for processing records of a specific type from a stream read operation.
/// Extends <see cref="IRecordProcessor"/> with type-safe record processing.
/// </summary>
/// <typeparam name="T">The CLR type of the record value to process.</typeparam>
/// <remarks>
/// <para>
/// Only <see cref="ProcessRecord"/> must be implemented. Lifecycle methods are inherited
/// from <see cref="IRecordProcessor"/> with default no-op implementations.
/// </para>
/// <para>
/// The default <see cref="IRecordProcessor.ProcessRecords"/> implementation filters records
/// by type and calls <see cref="ProcessRecord"/> for each matching record.
/// </para>
/// <para>
/// Use <see cref="DispatchingRecordProcessor"/> to register multiple typed processors
/// and route records automatically based on <see cref="Record.ValueType"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Minimal implementation - only ProcessRecord is required
/// public class OrderPlacedProcessor : IRecordProcessor&lt;OrderPlaced&gt; {
///     public ValueTask ProcessRecord(OrderPlaced value, ProcessRecordsContext ctx) {
///         Console.WriteLine($"Order {value.OrderId} placed");
///         return ValueTask.CompletedTask;
///     }
/// }
///
/// // Full implementation with lifecycle handling
/// public class OrderProcessor : IRecordProcessor&lt;OrderPlaced&gt; {
///     public ValueTask Initialize(InitializationContext ctx) {
///         Console.WriteLine($"Starting: {ctx.ProcessId}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask ProcessRecord(OrderPlaced value, ProcessRecordsContext ctx) {
///         Console.WriteLine($"Order {value.OrderId}");
///         return ValueTask.CompletedTask;
///     }
///
///     public ValueTask OnShutdown(ShutdownContext ctx) {
///         Console.WriteLine($"Processed {ctx.TotalRecords} records");
///         return ValueTask.CompletedTask;
///     }
/// }
/// </code>
/// </example>
[PublicAPI]
public interface IRecordProcessor<in T> : IRecordProcessor where T : class {
    /// <summary>
    /// Processes a single record of type <typeparamref name="T"/>.
    /// </summary>
    /// <param name="value">The strongly-typed deserialized record value.</param>
    /// <param name="context">Context containing records batch, lag information, and cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    ValueTask ProcessRecord(T value, ProcessRecordsContext context);

    /// <inheritdoc />
    /// <remarks>
    /// Default implementation filters records by type and calls <see cref="ProcessRecord"/>
    /// for each record where <see cref="Record.Value"/> is of type <typeparamref name="T"/>.
    /// </remarks>
    async ValueTask IRecordProcessor.ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records)
            if (record.Value is T typedValue)
                await ProcessRecord(typedValue, context).ConfigureAwait(false);
    }
}
