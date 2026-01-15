namespace Talyn;

#region Behavior Delegates

/// <summary>
/// Delegate to invoke the next behavior or terminal handler (fire-and-forget).
/// </summary>
/// <param name="ct">Cancellation token for the operation.</param>
/// <returns>A task that completes when the remaining pipeline has executed.</returns>
public delegate ValueTask BehaviorDelegate(CancellationToken ct);

/// <summary>
/// Delegate to invoke the next behavior or terminal handler (request/response).
/// </summary>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="ct">Cancellation token for the operation.</param>
/// <returns>A task containing the response from the remaining pipeline.</returns>
public delegate ValueTask<TResponse> BehaviorDelegate<TResponse>(CancellationToken ct);

#endregion

#region Behavior Interfaces

/// <summary>
/// Type-safe behavior for fire-and-forget messages (Transmit pattern).
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <remarks>
/// <para>
/// Unlike <see cref="ITalynMiddleware"/> which operates on untyped context,
/// this interface provides compile-time type safety and can be constrained
/// (e.g., <c>where TMessage : IValidatable</c>).
/// </para>
/// <para>
/// Behaviors execute in registration order, wrapping the receivers:
/// </para>
/// <list type="bullet">
///   <item><description>Execute logic before calling <c>next()</c></description></item>
///   <item><description>Skip downstream processing by not calling <c>next()</c> (short-circuit)</description></item>
///   <item><description>Execute logic after <c>next()</c> returns</description></item>
///   <item><description>Catch and handle exceptions from <c>next()</c></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class LoggingBehavior&lt;TMessage&gt; : ITalynBehavior&lt;TMessage&gt; {
///     public async ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct) {
///         Console.WriteLine($"Before: {typeof(TMessage).Name}");
///         await next(ct);
///         Console.WriteLine($"After: {typeof(TMessage).Name}");
///     }
/// }
/// </code>
/// </example>
public interface ITalynBehavior<in TMessage> {
    /// <summary>
    /// Handles the message, optionally calling the next behavior in the pipeline.
    /// </summary>
    /// <param name="message">The strongly-typed message.</param>
    /// <param name="next">Delegate to invoke the next behavior or receivers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when behavior processing is finished.</returns>
    ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct);
}

/// <summary>
/// Type-safe behavior for request/response messages (Broadcast pattern).
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// The response flows through the pipeline, enabling transformation,
/// enrichment, or caching of responses by behaviors. This follows the
/// MediatR <c>IPipelineBehavior&lt;TRequest, TResponse&gt;</c> pattern.
/// </para>
/// <para>
/// When multiple receivers exist, this behavior works with the first
/// successful response. Use <see cref="ITalynBroadcastBehavior{TMessage,TResponse}"/>
/// when you need access to all receiver responses with attribution.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class ValidationBehavior&lt;TMessage, TResponse&gt; : ITalynBehavior&lt;TMessage, TResponse&gt;
///     where TMessage : IValidatable
/// {
///     public async ValueTask&lt;TResponse&gt; HandleAsync(
///         TMessage message,
///         BehaviorDelegate&lt;TResponse&gt; next,
///         CancellationToken ct
///     ) {
///         message.Validate();  // Compile-time type safety!
///         var response = await next(ct);
///         return response;
///     }
/// }
/// </code>
/// </example>
public interface ITalynBehavior<in TMessage, TResponse> {
    /// <summary>
    /// Handles the message and returns the (potentially transformed) response.
    /// </summary>
    /// <param name="message">The strongly-typed message.</param>
    /// <param name="next">Delegate to invoke the next behavior or receivers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response, potentially transformed by this behavior.</returns>
    ValueTask<TResponse> HandleAsync(TMessage message, BehaviorDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>
/// Type-safe behavior with full access to all receiver responses with attribution.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// <para>
/// Use this interface when you need:
/// </para>
/// <list type="bullet">
///   <item><description>Timing data for each receiver</description></item>
///   <item><description>Per-receiver error handling</description></item>
///   <item><description>Response aggregation across multiple receivers</description></item>
///   <item><description>Knowledge of which receiver type produced each response</description></item>
/// </list>
/// <para>
/// For simpler scenarios where you only need the first successful response,
/// use <see cref="ITalynBehavior{TMessage,TResponse}"/> instead.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public class MetricsBehavior&lt;TMessage, TResponse&gt; : ITalynBroadcastBehavior&lt;TMessage, TResponse&gt; {
///     public async ValueTask&lt;ReceiverReply&lt;TResponse&gt;[]&gt; HandleAsync(
///         TMessage message,
///         BehaviorDelegate&lt;ReceiverReply&lt;TResponse&gt;[]&gt; next,
///         CancellationToken ct
///     ) {
///         var results = await next(ct);
///         foreach (var reply in results) {
///             _metrics.RecordLatency(reply.ReceiverType, reply.Duration);
///         }
///         return results;
///     }
/// }
/// </code>
/// </example>
public interface ITalynBroadcastBehavior<in TMessage, TResponse> {
    /// <summary>
    /// Handles the message with access to all receiver responses.
    /// </summary>
    /// <param name="message">The strongly-typed message.</param>
    /// <param name="next">Delegate to invoke the next behavior or receivers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All receiver responses with attribution.</returns>
    ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
        TMessage message,
        BehaviorDelegate<ReceiverReply<TResponse>[]> next,
        CancellationToken ct
    );
}

#endregion

#region Behavior Entries (Internal)

/// <summary>
/// Internal wrapper for fire-and-forget behavior instances with type metadata.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
internal readonly struct BehaviorEntry<TMessage> {
    /// <summary>
    /// The behavior instance.
    /// </summary>
    public required ITalynBehavior<TMessage> Behavior { get; init; }

    /// <summary>
    /// The concrete type of the behavior implementation.
    /// </summary>
    public required Type BehaviorType { get; init; }
}

/// <summary>
/// Internal wrapper for request/response behavior instances with type metadata.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal readonly struct BehaviorEntry<TMessage, TResponse> {
    /// <summary>
    /// The behavior instance.
    /// </summary>
    public required ITalynBehavior<TMessage, TResponse> Behavior { get; init; }

    /// <summary>
    /// The concrete type of the behavior implementation.
    /// </summary>
    public required Type BehaviorType { get; init; }
}

/// <summary>
/// Internal wrapper for broadcast behavior instances with type metadata.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal readonly struct BroadcastBehaviorEntry<TMessage, TResponse> {
    /// <summary>
    /// The behavior instance.
    /// </summary>
    public required ITalynBroadcastBehavior<TMessage, TResponse> Behavior { get; init; }

    /// <summary>
    /// The concrete type of the behavior implementation.
    /// </summary>
    public required Type BehaviorType { get; init; }
}

#endregion

#region Lambda Behavior Adapters

/// <summary>
/// Message behavior adapter wrapping a delegate.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <param name="handler">The handler delegate.</param>
internal sealed class LambdaBehavior<TMessage>(
    Func<TMessage, BehaviorDelegate, CancellationToken, ValueTask> handler
) : ITalynBehavior<TMessage> {
    public ValueTask HandleAsync(TMessage message, BehaviorDelegate next, CancellationToken ct) =>
        handler(message, next, ct);
}

/// <summary>
/// Request behavior adapter wrapping a delegate.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="handler">The handler delegate.</param>
internal sealed class LambdaBehavior<TMessage, TResponse>(
    Func<TMessage, BehaviorDelegate<TResponse>, CancellationToken, ValueTask<TResponse>> handler
) : ITalynBehavior<TMessage, TResponse> {
    public ValueTask<TResponse> HandleAsync(TMessage message, BehaviorDelegate<TResponse> next, CancellationToken ct) =>
        handler(message, next, ct);
}

/// <summary>
/// Broadcast behavior adapter wrapping a delegate.
/// </summary>
/// <typeparam name="TMessage">The message type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="handler">The handler delegate.</param>
internal sealed class LambdaBroadcastBehavior<TMessage, TResponse>(
    Func<TMessage, BehaviorDelegate<ReceiverReply<TResponse>[]>, CancellationToken, ValueTask<ReceiverReply<TResponse>[]>> handler
) : ITalynBroadcastBehavior<TMessage, TResponse> {
    public ValueTask<ReceiverReply<TResponse>[]> HandleAsync(
        TMessage message,
        BehaviorDelegate<ReceiverReply<TResponse>[]> next,
        CancellationToken ct
    ) => handler(message, next, ct);
}

#endregion
