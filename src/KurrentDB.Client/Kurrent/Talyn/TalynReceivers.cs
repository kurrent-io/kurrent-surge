namespace Talyn;

#region Receiver Contracts

/// <summary>
/// Receives and handles messages of type <typeparamref name="T"/> (fire-and-forget).
/// </summary>
/// <typeparam name="T">The message type to receive.</typeparam>
/// <remarks>
/// Implement this interface for notification-style messages that don't require a response.
/// Use with <see cref="ITalynBus.TransmitAsync{T}(T,CancellationToken)"/> or
/// <see cref="ITalynMediator.InvokeAsync{T}"/>.
/// </remarks>
public interface IReceiver<in T> {
    /// <summary>
    /// Handles the received message.
    /// </summary>
    /// <param name="message">The message to handle.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when handling is finished.</returns>
    ValueTask HandleAsync(T message, CancellationToken ct);
}

/// <summary>
/// Receives requests of type <typeparamref name="T"/> and returns responses of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="T">The request type to receive.</typeparam>
/// <typeparam name="TResponse">The response type to return.</typeparam>
/// <remarks>
/// Implement this interface for request/response patterns.
/// Use with <see cref="ITalynBus.BroadcastAsync{T,TResponse}(T,ExecutionMode?,CancellationToken)"/> or
/// <see cref="ITalynMediator.ScatterAsync{T,TResponse}"/>.
/// </remarks>
public interface IReceiver<in T, TResponse> {
    /// <summary>
    /// Handles the received request and returns a response.
    /// </summary>
    /// <param name="message">The request message to handle.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task containing the response value.</returns>
    ValueTask<TResponse> HandleAsync(T message, CancellationToken ct);
}

#endregion

#region Handler Delegates

/// <summary>
/// Async message handler delegate.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="message">The message to handle.</param>
/// <param name="ct">Cancellation token.</param>
public delegate ValueTask MessageHandler<in T>(T message, CancellationToken ct);

/// <summary>
/// Async message handler delegate with state (zero-allocation pattern).
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="state">The state passed on each invocation.</param>
/// <param name="message">The message to handle.</param>
/// <param name="ct">Cancellation token.</param>
public delegate ValueTask MessageHandler<in TState, in T>(TState state, T message, CancellationToken ct);

/// <summary>
/// Sync message handler delegate.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="message">The message to handle.</param>
public delegate void SyncMessageHandler<in T>(T message);

/// <summary>
/// Sync message handler delegate with state (zero-allocation pattern).
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="state">The state passed on each invocation.</param>
/// <param name="message">The message to handle.</param>
public delegate void SyncMessageHandler<in TState, in T>(TState state, T message);

/// <summary>
/// Async request handler delegate.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="message">The request message.</param>
/// <param name="ct">Cancellation token.</param>
public delegate ValueTask<TResponse> RequestHandler<in T, TResponse>(T message, CancellationToken ct);

/// <summary>
/// Async request handler delegate with state (zero-allocation pattern).
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="state">The state passed on each invocation.</param>
/// <param name="message">The request message.</param>
/// <param name="ct">Cancellation token.</param>
public delegate ValueTask<TResponse> RequestHandler<in TState, in T, TResponse>(TState state, T message, CancellationToken ct);

/// <summary>
/// Sync request handler delegate.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="message">The request message.</param>
public delegate TResponse SyncRequestHandler<in T, out TResponse>(T message);

/// <summary>
/// Sync request handler delegate with state (zero-allocation pattern).
/// </summary>
/// <typeparam name="TState">The state type.</typeparam>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="state">The state passed on each invocation.</param>
/// <param name="message">The request message.</param>
public delegate TResponse SyncRequestHandler<in TState, in T, out TResponse>(TState state, T message);

#endregion

#region Lambda Receiver Adapters

/// <summary>
/// Message receiver adapter wrapping a <see cref="MessageHandler{T}"/> delegate.
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
/// <param name="handler">The handler delegate.</param>
internal sealed class LambdaReceiver<T>(MessageHandler<T> handler) : IReceiver<T> {
    public ValueTask HandleAsync(T message, CancellationToken ct) => handler(message, ct);
}

/// <summary>
/// Request receiver adapter wrapping a <see cref="RequestHandler{T,TResponse}"/> delegate.
/// </summary>
/// <typeparam name="T">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <param name="handler">The handler delegate.</param>
internal sealed class LambdaRequestReceiver<T, TResponse>(RequestHandler<T, TResponse> handler) : IReceiver<T, TResponse> {
    public ValueTask<TResponse> HandleAsync(T message, CancellationToken ct) => handler(message, ct);
}

#endregion

#region Receiver Registration Extensions

/// <summary>
/// Fluent extension methods for receiver registration on <see cref="ITalynBus"/>.
/// </summary>
/// <remarks>
/// These methods provide syntactic sugar for <see cref="ITalynBus.Connect{T}"/> and
/// <see cref="ITalynBus.Connect{T,TResponse}"/> with a more fluent naming convention.
/// </remarks>
public static class TalynBusReceiverExtensions {
    /// <summary>
    /// Fluent receiver registration for message receivers.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="receiver">The receiver to connect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    /// <remarks>
    /// This is syntactic sugar for <see cref="ITalynBus.Connect{T}"/>.
    /// </remarks>
    public static ITalynBus WithReceiver<T>(this ITalynBus bus, IReceiver<T> receiver) => bus.Connect(receiver);

    /// <summary>
    /// Fluent receiver registration for request/response receivers.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="receiver">The receiver to connect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    /// <remarks>
    /// This is syntactic sugar for <see cref="ITalynBus.Connect{T,TResponse}"/>.
    /// </remarks>
    public static ITalynBus WithReceiver<T, TResponse>(this ITalynBus bus, IReceiver<T, TResponse> receiver) => bus.Connect(receiver);

    #region Lambda Receivers (Zero-Allocation with TState)

    /// <summary>
    /// Connects a lambda message handler with state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <typeparam name="TState">The state type passed to the handler.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="state">The state to pass to the handler on each invocation.</param>
    /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// bus.Connect&lt;OrderPlaced, OrderService&gt;(
    ///     orderService,
    ///     static async (svc, msg, ct) =&gt; await svc.HandleAsync(msg, ct)
    /// );
    /// </code>
    /// </example>
    public static ITalynBus Connect<T, TState>(
        this ITalynBus bus,
        TState state,
        MessageHandler<TState, T> handler
    ) => bus.Connect(new LambdaReceiver<T>((msg, ct) => handler(state, msg, ct)));

    /// <summary>
    /// Connects a lambda message handler without state.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="handler">The handler delegate.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T>(
        this ITalynBus bus,
        MessageHandler<T> handler
    ) => bus.Connect(new LambdaReceiver<T>(handler));

    /// <summary>
    /// Connects a synchronous lambda message handler with state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <typeparam name="TState">The state type passed to the handler.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="state">The state to pass to the handler on each invocation.</param>
    /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T, TState>(
        this ITalynBus bus,
        TState state,
        SyncMessageHandler<TState, T> handler
    ) => bus.Connect(new LambdaReceiver<T>((msg, _) => {
        handler(state, msg);
        return ValueTask.CompletedTask;
    }));

    /// <summary>
    /// Connects a synchronous lambda message handler without state.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="handler">The synchronous handler delegate.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T>(
        this ITalynBus bus,
        SyncMessageHandler<T> handler
    ) => bus.Connect(new LambdaReceiver<T>((msg, _) => {
        handler(msg);
        return ValueTask.CompletedTask;
    }));

    /// <summary>
    /// Connects a lambda request handler with state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The state type passed to the handler.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="state">The state to pass to the handler on each invocation.</param>
    /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    /// <example>
    /// <code>
    /// bus.Connect&lt;GetOrder, Order, DbContext&gt;(
    ///     dbContext,
    ///     static async (ctx, query, ct) =&gt; await ctx.Orders.FindAsync(query.Id, ct)
    /// );
    /// </code>
    /// </example>
    public static ITalynBus Connect<T, TResponse, TState>(
        this ITalynBus bus,
        TState state,
        RequestHandler<TState, T, TResponse> handler
    ) => bus.Connect(new LambdaRequestReceiver<T, TResponse>((msg, ct) => handler(state, msg, ct)));

    /// <summary>
    /// Connects a lambda request handler without state.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="handler">The handler delegate.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T, TResponse>(
        this ITalynBus bus,
        RequestHandler<T, TResponse> handler
    ) => bus.Connect(new LambdaRequestReceiver<T, TResponse>(handler));

    /// <summary>
    /// Connects a synchronous lambda request handler with state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The state type passed to the handler.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="state">The state to pass to the handler on each invocation.</param>
    /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T, TResponse, TState>(
        this ITalynBus bus,
        TState state,
        SyncRequestHandler<TState, T, TResponse> handler
    ) => bus.Connect(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(state, msg))));

    /// <summary>
    /// Connects a synchronous lambda request handler without state.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="bus">The bus instance.</param>
    /// <param name="handler">The synchronous handler delegate.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    public static ITalynBus Connect<T, TResponse>(
        this ITalynBus bus,
        SyncRequestHandler<T, TResponse> handler
    ) => bus.Connect(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(msg))));

    #endregion

    #region WithReceiver Lambda Overloads

    /// <summary>
    /// Fluent lambda receiver registration with state (zero-allocation pattern).
    /// </summary>
    public static ITalynBus WithReceiver<T, TState>(
        this ITalynBus bus,
        TState state,
        MessageHandler<TState, T> handler
    ) => bus.Connect<T, TState>(state, handler);

    /// <summary>
    /// Fluent lambda receiver registration without state.
    /// </summary>
    public static ITalynBus WithReceiver<T>(
        this ITalynBus bus,
        MessageHandler<T> handler
    ) => bus.Connect(handler);

    /// <summary>
    /// Fluent synchronous lambda receiver registration with state (zero-allocation pattern).
    /// </summary>
    public static ITalynBus WithReceiver<T, TState>(
        this ITalynBus bus,
        TState state,
        SyncMessageHandler<TState, T> handler
    ) => bus.Connect<T, TState>(state, handler);

    /// <summary>
    /// Fluent synchronous lambda receiver registration without state.
    /// </summary>
    public static ITalynBus WithReceiver<T>(
        this ITalynBus bus,
        SyncMessageHandler<T> handler
    ) => bus.Connect(handler);

    /// <summary>
    /// Fluent lambda request receiver registration with state (zero-allocation pattern).
    /// </summary>
    public static ITalynBus WithReceiver<T, TResponse, TState>(
        this ITalynBus bus,
        TState state,
        RequestHandler<TState, T, TResponse> handler
    ) => bus.Connect<T, TResponse, TState>(state, handler);

    /// <summary>
    /// Fluent lambda request receiver registration without state.
    /// </summary>
    public static ITalynBus WithReceiver<T, TResponse>(
        this ITalynBus bus,
        RequestHandler<T, TResponse> handler
    ) => bus.Connect(handler);

    /// <summary>
    /// Fluent synchronous lambda request receiver registration with state (zero-allocation pattern).
    /// </summary>
    public static ITalynBus WithReceiver<T, TResponse, TState>(
        this ITalynBus bus,
        TState state,
        SyncRequestHandler<TState, T, TResponse> handler
    ) => bus.Connect<T, TResponse, TState>(state, handler);

    /// <summary>
    /// Fluent synchronous lambda request receiver registration without state.
    /// </summary>
    public static ITalynBus WithReceiver<T, TResponse>(
        this ITalynBus bus,
        SyncRequestHandler<T, TResponse> handler
    ) => bus.Connect(handler);

    #endregion
}

#endregion
