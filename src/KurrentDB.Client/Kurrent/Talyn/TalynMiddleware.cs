using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Talyn;

#region Middleware Interface

/// <summary>
/// Delegate representing the next middleware or terminal handler in the pipeline.
/// </summary>
/// <param name="context">The message context flowing through the pipeline.</param>
/// <returns>A task that completes when the remaining pipeline has executed.</returns>
public delegate ValueTask TalynMiddlewareDelegate(TalynMessageContext context);

/// <summary>
/// Middleware that intercepts message dispatch in the Talyn bus pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Middleware executes in registration order, wrapping the entire dispatch to all receivers.
/// Each middleware can:
/// </para>
/// <list type="bullet">
///   <item><description>Execute logic before calling <c>next()</c></description></item>
///   <item><description>Skip downstream processing by not calling <c>next()</c> (short-circuit)</description></item>
///   <item><description>Execute logic after <c>next()</c> returns</description></item>
///   <item><description>Catch and handle exceptions from <c>next()</c></description></item>
///   <item><description>Share state with other middleware via <see cref="TalynMessageContext.Items"/></description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// public class TimingMiddleware : ITalynMiddleware {
///     public async ValueTask InvokeAsync(TalynMessageContext context, TalynMiddlewareDelegate next) {
///         var sw = Stopwatch.GetTimestamp();
///         await next(context);
///         var elapsed = Stopwatch.GetElapsedTime(sw);
///         Console.WriteLine($"Dispatched {context.MessageType.Name} in {elapsed.TotalMilliseconds}ms");
///     }
/// }
/// </code>
/// </example>
public interface ITalynMiddleware {
    /// <summary>
    /// Invokes the middleware logic.
    /// </summary>
    /// <param name="context">The message context containing the message and metadata.</param>
    /// <param name="next">Delegate to invoke the next middleware or terminal handler.</param>
    /// <returns>A task that completes when middleware processing is finished.</returns>
    ValueTask InvokeAsync(TalynMessageContext context, TalynMiddlewareDelegate next);
}

#endregion

#region Dispatch Kind

/// <summary>
/// Specifies the kind of dispatch operation being performed.
/// </summary>
public enum DispatchKind {
    /// <summary>
    /// Fire-and-forget message dispatch via <see cref="ITalynBus.TransmitAsync{T}(T,CancellationToken)"/>
    /// or <see cref="ITalynMediator.InvokeAsync{T}"/>.
    /// Receivers implement <see cref="IReceiver{T}"/> and do not return responses.
    /// </summary>
    Transmit,

    /// <summary>
    /// Request/response dispatch via <see cref="ITalynBus.BroadcastAsync{T,TResponse}(T,ExecutionMode?,CancellationToken)"/>
    /// or <see cref="ITalynMediator.ScatterAsync{T,TResponse}"/>.
    /// Receivers implement <see cref="IReceiver{T,TResponse}"/> and return attributed responses.
    /// </summary>
    Broadcast
}

#endregion

#region Message Context

/// <summary>
/// Context for a message being dispatched through the Talyn bus middleware pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This context is created per-message dispatch and flows through all middleware.
/// Use <see cref="Items"/> to share state between middleware components.
/// </para>
/// <para>
/// <b>Thread Safety:</b> The <see cref="Items"/> dictionary is NOT thread-safe.
/// Middleware executes sequentially in the pipeline, so concurrent access is not expected
/// during normal operation.
/// </para>
/// </remarks>
public sealed class TalynMessageContext {
    Dictionary<string, object?>? _items;

    internal TalynMessageContext(
        object message,
        Type messageType,
        DispatchKind dispatchKind,
        ExecutionMode executionMode,
        int receiverCount,
        CancellationToken cancellationToken
    ) {
        Message           = message;
        MessageType       = messageType;
        DispatchKind      = dispatchKind;
        ExecutionMode     = executionMode;
        ReceiverCount     = receiverCount;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// The message being dispatched.
    /// </summary>
    public object Message { get; }

    /// <summary>
    /// The runtime type of the message.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    /// The kind of dispatch operation being performed.
    /// </summary>
    public DispatchKind DispatchKind { get; }

    /// <summary>
    /// The execution mode for multi-receiver operations.
    /// </summary>
    public ExecutionMode ExecutionMode { get; }

    /// <summary>
    /// The number of receivers that will process this message.
    /// </summary>
    public int ReceiverCount { get; }

    /// <summary>
    /// The cancellation token for this dispatch operation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Indicates whether the dispatch was short-circuited by middleware.
    /// When <c>true</c>, receivers were not invoked.
    /// </summary>
    public bool IsShortCircuited { get; set; }

    /// <summary>
    /// Optional result set by middleware during short-circuit.
    /// For <see cref="DispatchKind.Broadcast"/> operations, middleware can provide
    /// a <see cref="ReceiverReply{TResponse}"/>[] result without invoking receivers.
    /// </summary>
    public object? ShortCircuitResult { get; set; }

    /// <summary>
    /// Gets the strongly-typed message.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <returns>The message cast to the specified type.</returns>
    /// <exception cref="InvalidCastException">When the message is not of type <typeparamref name="T"/>.</exception>
    public T GetMessage<T>() => (T)Message;

    /// <summary>
    /// Tries to get the strongly-typed message.
    /// </summary>
    /// <typeparam name="T">The expected message type.</typeparam>
    /// <param name="message">The message if cast succeeds.</param>
    /// <returns><c>true</c> if cast succeeded; otherwise <c>false</c>.</returns>
    public bool TryGetMessage<T>([MaybeNullWhen(false)] out T message) {
        if (Message is T typed) {
            message = typed;
            return true;
        }

        message = default;
        return false;
    }

    /// <summary>
    /// Dictionary for sharing state between middleware components.
    /// Lazily allocated on first access to avoid allocation when unused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This dictionary is NOT thread-safe. Middleware executes sequentially in the pipeline,
    /// so concurrent access is not expected during normal operation.
    /// </para>
    /// </remarks>
    public IDictionary<string, object?> Items =>
        _items ?? Interlocked.CompareExchange(ref _items, new Dictionary<string, object?>(), null) ?? _items!;

    /// <summary>
    /// Checks if the Items dictionary has been allocated.
    /// Use this to avoid allocating the dictionary when only reading.
    /// </summary>
    public bool HasItems => _items is not null;

    /// <summary>
    /// Tries to get a value from the Items dictionary without allocating if empty.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value if found and cast succeeds.</param>
    /// <returns><c>true</c> if found and cast succeeded; otherwise <c>false</c>.</returns>
    public bool TryGetItem<T>(string key, [MaybeNullWhen(false)] out T value) {
        if (_items is not null && _items.TryGetValue(key, out var obj) && obj is T typed) {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Sets a value in the Items dictionary.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store.</param>
    public void SetItem<T>(string key, T value) => Items[key] = value;
}

#endregion

#region Lambda Middleware

/// <summary>
/// Adapter that wraps a delegate as an <see cref="ITalynMiddleware"/>.
/// </summary>
internal sealed class LambdaMiddleware(
    Func<TalynMessageContext, TalynMiddlewareDelegate, ValueTask> handler
) : ITalynMiddleware {
    public ValueTask InvokeAsync(TalynMessageContext context, TalynMiddlewareDelegate next) =>
        handler(context, next);
}

#endregion
