using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;

namespace Talyn;

#region Bus & Options

/// <summary>
/// Specifies how multiple receivers execute when broadcasting or scattering.
/// </summary>
public enum ExecutionMode {
    /// <summary>
    /// Executes receivers one-by-one with <c>foreach + await</c>.
    /// Results maintain the same order as the receivers. Best for ordered processing and debugging.
    /// </summary>
    Sequential,

    /// <summary>
    /// Executes receivers with true thread parallelism via <see cref="Parallel.ForEachAsync{TSource}"/>.
    /// Best for CPU-bound receivers that benefit from multi-threading.
    /// </summary>
    Parallel,

    /// <summary>
    /// Starts all async operations concurrently and awaits them together.
    /// Best for I/O-bound receivers where overlapping async operations improves throughput.
    /// </summary>
    Concurrent
}

/// <summary>
/// Configuration options for the Talyn message bus.
/// </summary>
public sealed class TalynOptions {
    /// <summary>
    /// The bounded channel capacity that controls backpressure.
    /// When the channel is full, producers will wait until space is available.
    /// Defaults to <c>Environment.ProcessorCount * 2</c>.
    /// </summary>
    public int ChannelCapacity { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// Whether unexpected consumer exceptions should crash the consumer.
    /// When <c>true</c>, exceptions propagate and stop processing.
    /// When <c>false</c>, exceptions are logged and processing continues.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool FailFast { get; set; } = true;

    /// <summary>
    /// Whether disposal should wait for all queued work to complete.
    /// When <c>true</c>, <see cref="ITalynBus.DisposeAsync"/> waits for pending work.
    /// When <c>false</c>, pending work is cancelled immediately.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool DrainOnDispose { get; set; } = true;

    /// <summary>
    /// The default execution mode for multi-receiver operations.
    /// Applies to <see cref="ITalynBus.BroadcastAsync{T,TResponse}(T,ExecutionMode?,CancellationToken)"/>
    /// and <see cref="ITalynMediator.ScatterAsync{T,TResponse}"/> when no mode is specified.
    /// Defaults to <see cref="ExecutionMode.Sequential"/>.
    /// </summary>
    public ExecutionMode DefaultExecutionMode { get; set; } = ExecutionMode.Sequential;
}

/// <summary>
/// Provides direct message handling that bypasses the channel queue.
/// Use for low-latency scenarios, in-memory processing, or testing where backpressure is not needed.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ITalynBus"/>, operations execute immediately on the calling thread
/// without going through the bounded channel. This eliminates queuing overhead but
/// provides no backpressure or ordered processing guarantees.
/// </para>
/// <para>
/// For production workloads requiring resilience, use <see cref="ITalynBus"/> instead.
/// </para>
/// </remarks>
public interface ITalynMediator {
    /// <summary>
    /// Scatters a request to all registered <see cref="IReceiver{T,TResponse}"/> receivers directly,
    /// bypassing the channel queue. Returns attributed responses with receiver type, outcome, and duration.
    /// </summary>
    /// <typeparam name="T">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="message">The request message to scatter to all receivers.</param>
    /// <param name="mode">
    /// The execution mode for multi-receiver operations.
    /// If <c>null</c>, uses <see cref="TalynOptions.DefaultExecutionMode"/>.
    /// </param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// An array of <see cref="ReceiverReply{TResponse}"/> containing each receiver's response,
    /// type, and execution duration. Returns an empty array if no receivers are registered.
    /// </returns>
    ValueTask<ReceiverReply<TResponse>[]> ScatterAsync<T, TResponse>(
        T message, ExecutionMode? mode = null, CancellationToken ct = default
    );

    /// <summary>
    /// Invokes a message on all registered <see cref="IReceiver{T}"/> receivers directly,
    /// bypassing the channel queue. Fire-and-forget semantics.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message to send to all receivers.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when all receivers have processed the message.</returns>
    ValueTask InvokeAsync<T>(T message, CancellationToken ct = default);
}

/// <summary>
/// Provides queued message handling with backpressure for production workloads requiring resilience.
/// </summary>
/// <remarks>
/// <para>
/// Messages flow through a bounded <see cref="System.Threading.Channels.Channel{T}"/> that provides:
/// </para>
/// <list type="bullet">
///   <item><description>Backpressure — when the queue is full, producers wait</description></item>
///   <item><description>Ordered processing — messages are processed in FIFO order</description></item>
///   <item><description>Single-threaded consumer — predictable, thread-safe execution</description></item>
/// </list>
/// <para>
/// For low-latency scenarios that don't need queuing, use the inherited <see cref="ITalynMediator"/> methods.
/// </para>
/// </remarks>
public interface ITalynBus : ITalynMediator, IAsyncDisposable {
    /// <summary>
    /// Connects a message receiver for runtime handling.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receiver">The receiver to connect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ITalynBus Connect<T>(IReceiver<T> receiver);

    /// <summary>
    /// Disconnects a previously connected message receiver.
    /// Sets the disconnect flag and removes immediately. Returns without waiting for active executions.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ITalynBus Disconnect<T>(IReceiver<T> receiver);

    /// <summary>
    /// Disconnects a previously connected message receiver and waits for any active execution to complete.
    /// After this method returns, the receiver is guaranteed to never be invoked again.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <param name="ct">A cancellation token to cancel waiting for active executions.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ValueTask<ITalynBus> DisconnectAsync<T>(IReceiver<T> receiver, CancellationToken ct = default);

    /// <summary>
    /// Connects a request receiver for runtime handling.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="receiver">The receiver to connect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ITalynBus Connect<T, TResponse>(IReceiver<T, TResponse> receiver);

    /// <summary>
    /// Disconnects a previously connected request receiver.
    /// Sets the disconnect flag and removes immediately. Returns without waiting for active executions.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ITalynBus Disconnect<T, TResponse>(IReceiver<T, TResponse> receiver);

    /// <summary>
    /// Disconnects a previously connected request receiver and waits for any active execution to complete.
    /// After this method returns, the receiver is guaranteed to never be invoked again.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="receiver">The receiver to disconnect.</param>
    /// <param name="ct">A cancellation token to cancel waiting for active executions.</param>
    /// <returns>The bus instance for fluent chaining.</returns>
    ValueTask<ITalynBus> DisconnectAsync<T, TResponse>(IReceiver<T, TResponse> receiver, CancellationToken ct = default);

    /// <summary>
    /// Broadcasts a request to all registered <see cref="IReceiver{T,TResponse}"/> receivers through the queue.
    /// Returns attributed responses with receiver type, outcome, and duration.
    /// </summary>
    /// <typeparam name="T">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="message">The request message to broadcast to all receivers.</param>
    /// <param name="mode">
    /// The execution mode for multi-receiver operations.
    /// If <c>null</c>, uses <see cref="TalynOptions.DefaultExecutionMode"/>.
    /// </param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>
    /// An array of <see cref="ReceiverReply{TResponse}"/> containing each receiver's response,
    /// type, and execution duration. Returns an empty array if no receivers are registered.
    /// </returns>
    ValueTask<ReceiverReply<TResponse>[]> BroadcastAsync<T, TResponse>(
        T message, ExecutionMode? mode = null, CancellationToken ct = default
    );

    /// <summary>
    /// Broadcasts a request to all registered <see cref="IReceiver{T,TResponse}"/> receivers through the queue,
    /// invoking a callback when complete. Fire-and-forget semantics — returns immediately after queuing.
    /// </summary>
    /// <typeparam name="T">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="message">The request message to broadcast to all receivers.</param>
    /// <param name="callback">
    /// A callback invoked when broadcasting completes. Receives an <see cref="Outcome{T}"/>
    /// containing either the array of replies or an exception.
    /// </param>
    /// <param name="mode">
    /// The execution mode for multi-receiver operations.
    /// If <c>null</c>, uses <see cref="TalynOptions.DefaultExecutionMode"/>.
    /// </param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the message is queued (not when processing completes).</returns>
    ValueTask BroadcastAsync<T, TResponse>(
        T message, OnOutcome<ReceiverReply<TResponse>[]> callback,
        ExecutionMode? mode = null, CancellationToken ct = default
    );

    /// <summary>
    /// Transmits a message to all registered <see cref="IReceiver{T}"/> receivers through the queue.
    /// Fire-and-forget semantics — message receivers do not return responses.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message to transmit to all receivers.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when all receivers have processed the message.</returns>
    ValueTask TransmitAsync<T>(T message, CancellationToken ct = default);

    /// <summary>
    /// Transmits a message to all registered <see cref="IReceiver{T}"/> receivers through the queue,
    /// invoking a callback when complete. Fire-and-forget semantics — returns immediately after queuing.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="message">The message to transmit to all receivers.</param>
    /// <param name="callback">
    /// A callback invoked when transmission completes. Receives an <see cref="Outcome"/>
    /// indicating success or containing an exception.
    /// </param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>A task that completes when the message is queued (not when processing completes).</returns>
    ValueTask TransmitAsync<T>(T message, OnOutcome callback, CancellationToken ct = default);
}

/// <summary>
/// High-performance message bus implementation using a bounded channel for backpressure.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe. Multiple producers can call
/// <see cref="TransmitAsync{T}(T,CancellationToken)"/> and <see cref="BroadcastAsync{T,TResponse}(T,ExecutionMode?,CancellationToken)"/>
/// concurrently. A single consumer processes messages in FIFO order.
/// </para>
/// <para>
/// <b>Disposal:</b> Call <see cref="DisposeAsync"/> to gracefully shut down.
/// If <see cref="TalynOptions.DrainOnDispose"/> is <c>true</c>, disposal waits for queued work to complete.
/// </para>
/// </remarks>
public sealed partial class TalynBus : ITalynBus {
    readonly Channel<IDirective>     _channel;
    readonly Task                    _consumer;
    readonly CancellationTokenSource _cts = new();
    readonly ILogger<TalynBus>       _logger;
    readonly ITalynMiddleware[]      _middleware;
    readonly TalynOptions            _options;
    readonly ITalynRegistry          _registry;

    /// <summary>
    /// Creates a new Talyn message bus with the specified registry and options.
    /// </summary>
    /// <param name="registry">The receiver registry for resolving message and request handlers.</param>
    /// <param name="options">Configuration options. If <c>null</c>, defaults are used.</param>
    /// <param name="logger">Optional logger for diagnostics. If <c>null</c>, logging is disabled.</param>
    /// <param name="middleware">Optional middleware pipeline. If <c>null</c>, no middleware is applied.</param>
    internal TalynBus(
        ITalynRegistry registry,
        TalynOptions? options = null,
        ILogger<TalynBus>? logger = null,
        ITalynMiddleware[]? middleware = null
    ) {
        _registry   = registry;
        _logger     = logger ?? NullLogger<TalynBus>.Instance;
        _options    = options ?? new TalynOptions();
        _middleware = middleware ?? [];

        _channel = Channel.CreateBounded<IDirective>(
            new BoundedChannelOptions(_options.ChannelCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            }
        );

        _consumer = Task.Run(ConsumeAsync);

        Log.ConsumerStarted(
            _logger, _options.ChannelCapacity, _options.FailFast,
            _options.DrainOnDispose
        );

        return;

        async Task ConsumeAsync() {
            var processed = 0L;

            try {
                await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
                    try {
                        var ct = _options.DrainOnDispose ? CancellationToken.None : _cts.Token;
                        await item.ExecuteAsync(ct).ConfigureAwait(false);
                        processed++;
                    }
                    catch (OperationCanceledException) when (_cts.IsCancellationRequested) {
                        Log.DirectiveCancelled(_logger, processed);
                    }
                    catch (Exception ex) {
                        Log.DirectiveFailed(_logger, ex, processed);
                        if (_options.FailFast) throw;
                    }
                    finally {
                        item.Dispose();
                    }
            }
            catch (Exception ex) when (!_options.FailFast) {
                Log.ConsumerFailed(_logger, ex, processed);
            }

            Log.ConsumerStopped(_logger, processed);
        }
    }

    #region Scatter (Direct - Multiple Receivers, Scatter-Gather)

    /// <inheritdoc />
    public ValueTask<ReceiverReply<TResponse>[]> ScatterAsync<T, TResponse>(
        T message,
        ExecutionMode? mode = null,
        CancellationToken ct = default
    ) {
        var entries           = _registry.GetReceivers<T, TResponse>();
        var simpleBehaviors   = _registry.GetBehaviors<T, TResponse>();
        var broadcastBehaviors = _registry.GetBroadcastBehaviors<T, TResponse>();

        if (entries.Length == 0)
            return ValueTask.FromResult(Array.Empty<ReceiverReply<TResponse>>());

        var executionMode = mode ?? _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && simpleBehaviors.Length == 0 && broadcastBehaviors.Length == 0)
            return ExecuteEntriesAsync(entries, message, executionMode, ct);

        // Behaviors-only path (no middleware)
        if (_middleware.Length == 0) {
            // Prefer broadcast behaviors if available (full attribution)
            if (broadcastBehaviors.Length > 0) {
                var pipeline = BuildBroadcastBehaviorPipeline(broadcastBehaviors, entries, message, executionMode);
                return pipeline(ct);
            }

            // Use simple behaviors
            return ExecuteWithSimpleBehaviorsAsync(simpleBehaviors, entries, message, executionMode, ct);
        }

        // Full path: middleware + behaviors
        return ExecuteWithBehaviorsAndMiddlewareAsync(
            simpleBehaviors, broadcastBehaviors, entries, message, executionMode, ct
        );
    }

    /// <summary>
    /// Executes simple behaviors for request/response and wraps the result.
    /// </summary>
    async ValueTask<ReceiverReply<TResponse>[]> ExecuteWithSimpleBehaviorsAsync<T, TResponse>(
        BehaviorEntry<T, TResponse>[] behaviors,
        ReceiverEntry<T, TResponse>[] entries,
        T message,
        ExecutionMode mode,
        CancellationToken ct
    ) {
        var pipeline = BuildSimpleBehaviorPipeline(behaviors, entries, message, mode);
        var response = await pipeline(ct).ConfigureAwait(false);
        return WrapResponse(response, typeof(ITalynBehavior<T, TResponse>));
    }

    #endregion

    #region Dispose

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        Log.Disposing(_logger, _options.DrainOnDispose);

        _channel.Writer.Complete();

        if (!_options.DrainOnDispose) {
            await _cts.CancelAsync().ConfigureAwait(false);
            Log.CancellationRequested(_logger);
        }

        await _consumer.ConfigureAwait(false);
        _cts.Dispose();

        Log.Disposed(_logger);
    }

    #endregion

    #region Static Factory

    /// <summary>
    /// Creates a builder for configuring a standalone Talyn bus without DI container.
    /// </summary>
    /// <returns>A <see cref="Builder"/> for fluent configuration.</returns>
    /// <remarks>
    /// <para>
    /// Use this factory method for standalone scenarios where you don't need DI integration.
    /// Configure options and register receivers, then call <see cref="Builder.Build"/>
    /// to create the bus instance.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ITalynBus bus = TalynBus.Create()
    ///     .WithReceiver&lt;MyMessage&gt;(new MyReceiver())
    ///     .WithReceiver&lt;OtherMessage&gt;(new OtherReceiver())
    ///     .Build();
    /// </code>
    /// </example>
    public static Builder Create() => new();

    /// <summary>
    /// Creates the bus from an internal builder context.
    /// </summary>
    internal static ITalynBus Create(
        TalynRegistry registry,
        TalynOptions options,
        ILogger<TalynBus>? logger,
        ITalynMiddleware[]? middleware = null
    ) =>
        new TalynBus(registry, options, logger, middleware);

    #endregion

    #region Nested Builder

    /// <summary>
    /// Builder for configuring a standalone Talyn bus without DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="TalynBus.Create"/> to obtain a builder instance.
    /// Configure options, register receivers, then call <see cref="Build"/> to create the bus.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ITalynBus bus = TalynBus.Create()
    ///     .WithOptions(new TalynOptions { ChannelCapacity = 100 })
    ///     .WithLogger(loggerFactory.CreateLogger&lt;TalynBus&gt;())
    ///     .WithReceiver&lt;MyMessage&gt;(new MyReceiver())
    ///     .WithReceiver&lt;GetOrder, Order&gt;(async (msg, ct) =&gt; await db.GetOrderAsync(msg.Id, ct))
    ///     .Build();
    /// </code>
    /// </example>
    public sealed class Builder {
        readonly List<ITalynMiddleware> _middleware = new();
        readonly TalynRegistry          _registry   = new();

        ILogger<TalynBus>? _logger;
        TalynOptions       _options = new();

        #region Configuration

        /// <summary>
        /// Configures the bus options.
        /// </summary>
        /// <param name="options">The options to use.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithOptions(TalynOptions options) {
            _options = options;
            return this;
        }

        /// <summary>
        /// Configures the bus options using a configuration action.
        /// </summary>
        /// <param name="configure">An action to configure the options.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithOptions(Action<TalynOptions> configure) {
            configure(_options);
            return this;
        }

        /// <summary>
        /// Sets the logger for diagnostics.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithLogger(ILogger<TalynBus> logger) {
            _logger = logger;
            return this;
        }

        #endregion

        #region Middleware

        /// <summary>
        /// Adds middleware to the pipeline. Middleware executes in registration order.
        /// </summary>
        /// <param name="middleware">The middleware instance to add.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// Middleware registered first executes outermost (first before, last after).
        /// </remarks>
        public Builder UseMiddleware(ITalynMiddleware middleware) {
            _middleware.Add(middleware);
            return this;
        }

        /// <summary>
        /// Adds a lambda middleware to the pipeline. Middleware executes in registration order.
        /// </summary>
        /// <param name="handler">The middleware handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        /// <example>
        /// <code>
        /// builder.Use(async (ctx, next) => {
        ///     var sw = Stopwatch.GetTimestamp();
        ///     await next(ctx);
        ///     Console.WriteLine($"Elapsed: {Stopwatch.GetElapsedTime(sw)}");
        /// });
        /// </code>
        /// </example>
        public Builder Use(Func<TalynMessageContext, TalynMiddlewareDelegate, ValueTask> handler) {
            _middleware.Add(new LambdaMiddleware(handler));
            return this;
        }

        #endregion

        #region Behaviors

        /// <summary>
        /// Registers a behavior instance for fire-and-forget messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <param name="behavior">The behavior instance.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// Behaviors execute in registration order, wrapping the receivers.
        /// First registered = outermost wrapper.
        /// </remarks>
        public Builder UseBehavior<TMessage>(ITalynBehavior<TMessage> behavior) {
            _registry.Connect(behavior);
            return this;
        }

        /// <summary>
        /// Registers a lambda behavior for fire-and-forget messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <param name="handler">The behavior handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        /// <example>
        /// <code>
        /// builder.UseBehavior&lt;OrderPlaced&gt;(async (msg, next, ct) => {
        ///     Console.WriteLine($"Before: {msg}");
        ///     await next(ct);
        ///     Console.WriteLine($"After: {msg}");
        /// });
        /// </code>
        /// </example>
        public Builder UseBehavior<TMessage>(
            Func<TMessage, BehaviorDelegate, CancellationToken, ValueTask> handler
        ) {
            _registry.Connect(new LambdaBehavior<TMessage>(handler));
            return this;
        }

        /// <summary>
        /// Registers a behavior instance for request/response messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="behavior">The behavior instance.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// This behavior works with the first successful response from receivers.
        /// Use <see cref="UseBroadcastBehavior{TMessage,TResponse}(ITalynBroadcastBehavior{TMessage,TResponse})"/>
        /// when you need access to all receiver responses with attribution.
        /// </para>
        /// </remarks>
        public Builder UseBehavior<TMessage, TResponse>(ITalynBehavior<TMessage, TResponse> behavior) {
            _registry.Connect(behavior);
            return this;
        }

        /// <summary>
        /// Registers a lambda behavior for request/response messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="handler">The behavior handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        /// <example>
        /// <code>
        /// builder.UseBehavior&lt;GetOrder, Order&gt;(async (msg, next, ct) => {
        ///     Console.WriteLine($"Before: {msg.Id}");
        ///     var response = await next(ct);
        ///     Console.WriteLine($"After: {response.Total}");
        ///     return response;
        /// });
        /// </code>
        /// </example>
        public Builder UseBehavior<TMessage, TResponse>(
            Func<TMessage, BehaviorDelegate<TResponse>, CancellationToken, ValueTask<TResponse>> handler
        ) {
            _registry.Connect(new LambdaBehavior<TMessage, TResponse>(handler));
            return this;
        }

        /// <summary>
        /// Registers a broadcast behavior instance for request/response messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="behavior">The behavior instance.</param>
        /// <returns>The builder for chaining.</returns>
        /// <remarks>
        /// <para>
        /// Broadcast behaviors have full access to all receiver responses with attribution,
        /// including timing data, error handling per-receiver, and response aggregation.
        /// Use <see cref="UseBehavior{TMessage,TResponse}(ITalynBehavior{TMessage,TResponse})"/>
        /// for simpler scenarios where you only need the first successful response.
        /// </para>
        /// </remarks>
        public Builder UseBroadcastBehavior<TMessage, TResponse>(ITalynBroadcastBehavior<TMessage, TResponse> behavior) {
            _registry.ConnectBroadcast(behavior);
            return this;
        }

        /// <summary>
        /// Registers a lambda broadcast behavior for request/response messages.
        /// </summary>
        /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="handler">The behavior handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        /// <example>
        /// <code>
        /// builder.UseBroadcastBehavior&lt;GetOrder, Order&gt;(async (msg, next, ct) => {
        ///     var results = await next(ct);
        ///     foreach (var reply in results) {
        ///         Console.WriteLine($"Receiver {reply.ReceiverType.Name}: {reply.Duration}");
        ///     }
        ///     return results;
        /// });
        /// </code>
        /// </example>
        public Builder UseBroadcastBehavior<TMessage, TResponse>(
            Func<TMessage, BehaviorDelegate<ReceiverReply<TResponse>[]>, CancellationToken, ValueTask<ReceiverReply<TResponse>[]>> handler
        ) {
            _registry.ConnectBroadcast(new LambdaBroadcastBehavior<TMessage, TResponse>(handler));
            return this;
        }

        #endregion

        #region Instance Receivers

        /// <summary>
        /// Registers a message receiver instance.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="receiver">The receiver instance.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T>(IReceiver<T> receiver) {
            _registry.Connect(receiver);
            return this;
        }

        /// <summary>
        /// Registers a request/response receiver instance.
        /// </summary>
        /// <typeparam name="T">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="receiver">The receiver instance.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TResponse>(IReceiver<T, TResponse> receiver) {
            _registry.Connect(receiver);
            return this;
        }

        #endregion

        #region Lambda Receivers (Async)

        /// <summary>
        /// Registers a lambda message handler.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="handler">The handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T>(MessageHandler<T> handler) {
            _registry.Connect(new LambdaReceiver<T>(handler));
            return this;
        }

        /// <summary>
        /// Registers a lambda message handler with state (zero-allocation pattern).
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">The state to pass to the handler on each invocation.</param>
        /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TState>(TState state, MessageHandler<TState, T> handler) {
            _registry.Connect(new LambdaReceiver<T>((msg, ct) => handler(state, msg, ct)));
            return this;
        }

        /// <summary>
        /// Registers a lambda request handler.
        /// </summary>
        /// <typeparam name="T">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="handler">The handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TResponse>(RequestHandler<T, TResponse> handler) {
            _registry.Connect(new LambdaRequestReceiver<T, TResponse>(handler));
            return this;
        }

        /// <summary>
        /// Registers a lambda request handler with state (zero-allocation pattern).
        /// </summary>
        /// <typeparam name="T">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">The state to pass to the handler on each invocation.</param>
        /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TResponse, TState>(TState state, RequestHandler<TState, T, TResponse> handler) {
            _registry.Connect(new LambdaRequestReceiver<T, TResponse>((msg, ct) => handler(state, msg, ct)));
            return this;
        }

        #endregion

        #region Lambda Receivers (Sync)

        /// <summary>
        /// Registers a synchronous lambda message handler.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="handler">The synchronous handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T>(SyncMessageHandler<T> handler) {
            _registry.Connect(new LambdaReceiver<T>((msg, _) => {
                handler(msg);
                return ValueTask.CompletedTask;
            }));
            return this;
        }

        /// <summary>
        /// Registers a synchronous lambda message handler with state (zero-allocation pattern).
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">The state to pass to the handler on each invocation.</param>
        /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TState>(TState state, SyncMessageHandler<TState, T> handler) {
            _registry.Connect(new LambdaReceiver<T>((msg, _) => {
                handler(state, msg);
                return ValueTask.CompletedTask;
            }));
            return this;
        }

        /// <summary>
        /// Registers a synchronous lambda request handler.
        /// </summary>
        /// <typeparam name="T">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="handler">The synchronous handler delegate.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TResponse>(SyncRequestHandler<T, TResponse> handler) {
            _registry.Connect(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(msg))));
            return this;
        }

        /// <summary>
        /// Registers a synchronous lambda request handler with state (zero-allocation pattern).
        /// </summary>
        /// <typeparam name="T">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <param name="state">The state to pass to the handler on each invocation.</param>
        /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
        /// <returns>The builder for chaining.</returns>
        public Builder WithReceiver<T, TResponse, TState>(TState state, SyncRequestHandler<TState, T, TResponse> handler) {
            _registry.Connect(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(state, msg))));
            return this;
        }

        #endregion

        #region Build

        /// <summary>
        /// Builds and returns the configured Talyn bus.
        /// </summary>
        /// <returns>A fully configured <see cref="ITalynBus"/> instance.</returns>
        public ITalynBus Build() =>
            new TalynBus(_registry, _options, _logger, _middleware.Count > 0 ? [.._middleware] : null);

        #endregion
    }

    #endregion

    #region Middleware Pipeline

    TalynMiddlewareDelegate BuildPipeline(TalynMiddlewareDelegate terminal) {
        var next = terminal;

        for (var i = _middleware.Length - 1; i >= 0; i--) {
            var middleware = _middleware[i];
            var current    = next;
            next = ctx => middleware.InvokeAsync(ctx, current);
        }

        return next;
    }

    async ValueTask InvokeWithMiddlewareAsync<T>(
        ReceiverEntry<T>[] entries,
        T message,
        ExecutionMode mode,
        DispatchKind kind,
        CancellationToken ct
    ) {
        var context = new TalynMessageContext(
            message!,
            typeof(T),
            kind,
            mode,
            entries.Length,
            ct
        );

        TalynMiddlewareDelegate terminal = async ctx => {
            if (!ctx.IsShortCircuited)
                await InvokeEntriesAsync(entries, (T)ctx.Message, mode, ctx.CancellationToken)
                    .ConfigureAwait(false);
        };

        var pipeline = BuildPipeline(terminal);
        await pipeline(context).ConfigureAwait(false);
    }

    async ValueTask<ReceiverReply<TResponse>[]> ExecuteWithMiddlewareAsync<T, TResponse>(
        ReceiverEntry<T, TResponse>[] entries,
        T message,
        ExecutionMode mode,
        CancellationToken ct
    ) {
        var context = new TalynMessageContext(
            message!,
            typeof(T),
            DispatchKind.Broadcast,
            mode,
            entries.Length,
            ct
        );

        ReceiverReply<TResponse>[]? result = null;

        TalynMiddlewareDelegate terminal = async ctx => {
            if (!ctx.IsShortCircuited) {
                result = await ExecuteEntriesAsync(entries, (T)ctx.Message, mode, ctx.CancellationToken)
                    .ConfigureAwait(false);
            }
        };

        var pipeline = BuildPipeline(terminal);
        await pipeline(context).ConfigureAwait(false);

        // If short-circuited, check for provided result
        if (context.IsShortCircuited && context.ShortCircuitResult is ReceiverReply<TResponse>[] shortCircuitResult)
            return shortCircuitResult;

        return result ?? [];
    }

    #endregion

    #region Behavior Pipeline

    /// <summary>
    /// Builds a behavior pipeline for fire-and-forget messages.
    /// </summary>
    BehaviorDelegate BuildBehaviorPipeline<T>(
        BehaviorEntry<T>[] behaviors,
        ReceiverEntry<T>[] receivers,
        T message,
        ExecutionMode mode
    ) {
        // Terminal: invoke receivers
        BehaviorDelegate terminal = ct =>
            InvokeEntriesAsync(receivers, message, mode, ct);

        // No behaviors? Return terminal directly (fast path)
        if (behaviors.Length == 0)
            return terminal;

        // Build pipeline from last to first so behaviors execute in registration order
        var next = terminal;
        for (var i = behaviors.Length - 1; i >= 0; i--) {
            var behavior = behaviors[i].Behavior;
            var current  = next;
            next = ct => behavior.HandleAsync(message, current, ct);
        }

        return next;
    }

    /// <summary>
    /// Builds a behavior pipeline for request/response messages with full attribution.
    /// Uses broadcast behaviors that work with ReceiverReply[] directly.
    /// </summary>
    BehaviorDelegate<ReceiverReply<TResponse>[]> BuildBroadcastBehaviorPipeline<T, TResponse>(
        BroadcastBehaviorEntry<T, TResponse>[] behaviors,
        ReceiverEntry<T, TResponse>[] receivers,
        T message,
        ExecutionMode mode
    ) {
        // Terminal: invoke receivers, collect responses
        BehaviorDelegate<ReceiverReply<TResponse>[]> terminal = ct =>
            ExecuteEntriesAsync(receivers, message, mode, ct);

        // No behaviors? Return terminal directly (fast path)
        if (behaviors.Length == 0)
            return terminal;

        // Build pipeline from last to first
        var next = terminal;
        for (var i = behaviors.Length - 1; i >= 0; i--) {
            var behavior = behaviors[i].Behavior;
            var current  = next;
            next = ct => behavior.HandleAsync(message, current, ct);
        }

        return next;
    }

    /// <summary>
    /// Builds a behavior pipeline for request/response messages with simple response extraction.
    /// Uses behaviors that work with single TResponse (first successful).
    /// </summary>
    BehaviorDelegate<TResponse> BuildSimpleBehaviorPipeline<T, TResponse>(
        BehaviorEntry<T, TResponse>[] behaviors,
        ReceiverEntry<T, TResponse>[] receivers,
        T message,
        ExecutionMode mode
    ) {
        // Terminal: invoke receivers, extract first successful response
        BehaviorDelegate<TResponse> terminal = async ct => {
            var results = await ExecuteEntriesAsync(receivers, message, mode, ct).ConfigureAwait(false);
            return ExtractFirstSuccess(results);
        };

        // No behaviors? Return terminal directly (fast path)
        if (behaviors.Length == 0)
            return terminal;

        // Build pipeline from last to first
        var next = terminal;
        for (var i = behaviors.Length - 1; i >= 0; i--) {
            var behavior = behaviors[i].Behavior;
            var current  = next;
            next = ct => behavior.HandleAsync(message, current, ct);
        }

        return next;
    }

    /// <summary>
    /// Extracts the first successful response from receiver replies.
    /// </summary>
    static TResponse ExtractFirstSuccess<TResponse>(ReceiverReply<TResponse>[] results) {
        foreach (var reply in results)
            if (reply.Result.IsSuccess)
                return reply.Result.Response;

        // If all failed, throw the first error
        if (results.Length > 0)
            return results[0].Result.GetValueOrThrow();

        throw new InvalidOperationException("No receivers responded");
    }

    /// <summary>
    /// Wraps a single response back into a ReceiverReply array.
    /// Used when simple behaviors transform the response.
    /// </summary>
    static ReceiverReply<TResponse>[] WrapResponse<TResponse>(TResponse response, Type behaviorType) =>
        [new ReceiverReply<TResponse>(behaviorType, Outcome<TResponse>.Success(response), TimeSpan.Zero)];

    /// <summary>
    /// Executes behaviors then middleware for fire-and-forget messages.
    /// Behaviors run inside the middleware pipeline (behaviors wrap receivers, middleware wraps everything).
    /// </summary>
    async ValueTask InvokeWithBehaviorsAndMiddlewareAsync<T>(
        BehaviorEntry<T>[] behaviors,
        ReceiverEntry<T>[] entries,
        T message,
        ExecutionMode mode,
        DispatchKind kind,
        CancellationToken ct
    ) {
        var context = new TalynMessageContext(
            message!,
            typeof(T),
            kind,
            mode,
            entries.Length,
            ct
        );

        TalynMiddlewareDelegate terminal = async ctx => {
            if (!ctx.IsShortCircuited) {
                var behaviorPipeline = BuildBehaviorPipeline(behaviors, entries, (T)ctx.Message, mode);
                await behaviorPipeline(ctx.CancellationToken).ConfigureAwait(false);
            }
        };

        var pipeline = BuildPipeline(terminal);
        await pipeline(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes behaviors then middleware for request/response messages.
    /// Behaviors run inside the middleware pipeline.
    /// </summary>
    async ValueTask<ReceiverReply<TResponse>[]> ExecuteWithBehaviorsAndMiddlewareAsync<T, TResponse>(
        BehaviorEntry<T, TResponse>[] simpleBehaviors,
        BroadcastBehaviorEntry<T, TResponse>[] broadcastBehaviors,
        ReceiverEntry<T, TResponse>[] entries,
        T message,
        ExecutionMode mode,
        CancellationToken ct
    ) {
        var context = new TalynMessageContext(
            message!,
            typeof(T),
            DispatchKind.Broadcast,
            mode,
            entries.Length,
            ct
        );

        ReceiverReply<TResponse>[]? result = null;

        TalynMiddlewareDelegate terminal = async ctx => {
            if (!ctx.IsShortCircuited) {
                // If we have broadcast behaviors, use them (full attribution)
                if (broadcastBehaviors.Length > 0) {
                    var pipeline = BuildBroadcastBehaviorPipeline(broadcastBehaviors, entries, (T)ctx.Message, mode);
                    result = await pipeline(ctx.CancellationToken).ConfigureAwait(false);
                }
                // Else if we have simple behaviors, use them
                else if (simpleBehaviors.Length > 0) {
                    var pipeline = BuildSimpleBehaviorPipeline(simpleBehaviors, entries, (T)ctx.Message, mode);
                    var response = await pipeline(ctx.CancellationToken).ConfigureAwait(false);
                    result = WrapResponse(response, typeof(ITalynBehavior<T, TResponse>));
                }
                // No behaviors - direct execution
                else {
                    result = await ExecuteEntriesAsync(entries, (T)ctx.Message, mode, ctx.CancellationToken)
                        .ConfigureAwait(false);
                }
            }
        };

        var pipeline = BuildPipeline(terminal);
        await pipeline(context).ConfigureAwait(false);

        // If short-circuited, check for provided result
        if (context.IsShortCircuited && context.ShortCircuitResult is ReceiverReply<TResponse>[] shortCircuitResult)
            return shortCircuitResult;

        return result ?? [];
    }

    #endregion

    #region Transmit (Message - Multiple Receivers)

    /// <inheritdoc />
    public ValueTask TransmitAsync<T>(T message, CancellationToken ct = default) {
        var entries   = _registry.GetReceivers<T>();
        var behaviors = _registry.GetBehaviors<T>();

        if (entries.Length == 0)
            return ValueTask.CompletedTask;

        var mode = _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && behaviors.Length == 0) {
            var item = Directive.Create(
                (message, entries, mode),
                static async (state, ct) => {
                    await InvokeEntriesAsync(state.entries, state.message, state.mode, ct)
                        .ConfigureAwait(false);
                    return true;
                }
            );

            var writeTask = _channel.Writer.WriteAsync(item, ct);
            return writeTask.IsCompletedSuccessfully
                ? AwaitItem(item)
                : AwaitWriteAndItem(writeTask, item);
        }

        // Behaviors-only path: no middleware but has behaviors
        if (_middleware.Length == 0) {
            var behaviorItem = Directive.Create(
                (bus: this, message, entries, behaviors, mode),
                static async (state, ct) => {
                    var pipeline = state.bus.BuildBehaviorPipeline(
                        state.behaviors, state.entries, state.message, state.mode
                    );
                    await pipeline(ct).ConfigureAwait(false);
                    return true;
                }
            );

            var behaviorWriteTask = _channel.Writer.WriteAsync(behaviorItem, ct);
            return behaviorWriteTask.IsCompletedSuccessfully
                ? AwaitItem(behaviorItem)
                : AwaitWriteAndItem(behaviorWriteTask, behaviorItem);
        }

        // Full path: middleware + behaviors
        var middlewareItem = Directive.Create(
            (bus: this, message, entries, behaviors, mode),
            static async (state, ct) => {
                await state.bus.InvokeWithBehaviorsAndMiddlewareAsync(
                    state.behaviors, state.entries, state.message, state.mode, DispatchKind.Transmit, ct
                ).ConfigureAwait(false);
                return true;
            }
        );

        var middlewareWriteTask = _channel.Writer.WriteAsync(middlewareItem, ct);
        return middlewareWriteTask.IsCompletedSuccessfully
            ? AwaitItem(middlewareItem)
            : AwaitWriteAndItem(middlewareWriteTask, middlewareItem);

        static async ValueTask AwaitItem(PooledDirective<bool> item) {
            await item.Task.ConfigureAwait(false);
        }

        static async ValueTask AwaitWriteAndItem(ValueTask writeTask, PooledDirective<bool> item) {
            await writeTask.ConfigureAwait(false);
            await item.Task.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask TransmitAsync<T>(
        T message,
        OnOutcome callback,
        CancellationToken ct = default
    ) {
        var entries   = _registry.GetReceivers<T>();
        var behaviors = _registry.GetBehaviors<T>();

        if (entries.Length == 0) {
            var callbackTask = callback(Outcome.Success());
            return callbackTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : callbackTask;
        }

        var mode = _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && behaviors.Length == 0) {
            var item = Directive.Create(
                (message, entries, callback, mode),
                static async (state, ct) => {
                    try {
                        await InvokeEntriesAsync(state.entries, state.message, state.mode, ct)
                            .ConfigureAwait(false);
                        await state.callback(Outcome.Success()).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        await state.callback(Outcome.Failure(ex)).ConfigureAwait(false);
                    }
                    return true;
                }
            );

            var writeTask = _channel.Writer.WriteAsync(item, ct);
            return writeTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : AwaitWrite(writeTask);
        }

        // Behaviors-only path: no middleware but has behaviors
        if (_middleware.Length == 0) {
            var behaviorItem = Directive.Create(
                (bus: this, message, entries, behaviors, callback, mode),
                static async (state, ct) => {
                    try {
                        var pipeline = state.bus.BuildBehaviorPipeline(
                            state.behaviors, state.entries, state.message, state.mode
                        );
                        await pipeline(ct).ConfigureAwait(false);
                        await state.callback(Outcome.Success()).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        await state.callback(Outcome.Failure(ex)).ConfigureAwait(false);
                    }
                    return true;
                }
            );

            var behaviorWriteTask = _channel.Writer.WriteAsync(behaviorItem, ct);
            return behaviorWriteTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : AwaitWrite(behaviorWriteTask);
        }

        // Full path: middleware + behaviors
        var middlewareItem = Directive.Create(
            (bus: this, message, entries, behaviors, callback, mode),
            static async (state, ct) => {
                try {
                    await state.bus.InvokeWithBehaviorsAndMiddlewareAsync(
                        state.behaviors, state.entries, state.message, state.mode, DispatchKind.Transmit, ct
                    ).ConfigureAwait(false);
                    await state.callback(Outcome.Success()).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    await state.callback(Outcome.Failure(ex)).ConfigureAwait(false);
                }
                return true;
            }
        );

        var middlewareWriteTask = _channel.Writer.WriteAsync(middlewareItem, ct);
        return middlewareWriteTask.IsCompletedSuccessfully
            ? ValueTask.CompletedTask
            : AwaitWrite(middlewareWriteTask);

        static async ValueTask AwaitWrite(ValueTask writeTask) {
            await writeTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Invokes receiver entries with lock-check pattern.
    /// </summary>
    static async ValueTask InvokeEntriesAsync<T>(
        ReceiverEntry<T>[] entries,
        T message,
        ExecutionMode mode,
        CancellationToken ct
    ) {
        if (entries.Length == 0)
            return;

        switch (mode) {
            case ExecutionMode.Sequential:
                foreach (var entry in entries) {
                    await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
                    try {
                        if (entry.IsDisconnected)
                            continue;

                        await entry.Receiver.HandleAsync(message, ct).ConfigureAwait(false);
                    }
                    finally {
                        entry.ExecutionLock.Release();
                    }
                }

                break;

            case ExecutionMode.Parallel:
                await Parallel.ForEachAsync(
                    entries, ct,
                    async (entry, token) => {
                        await entry.ExecutionLock.WaitAsync(token).ConfigureAwait(false);
                        try {
                            if (entry.IsDisconnected)
                                return;

                            await entry.Receiver.HandleAsync(message, token).ConfigureAwait(false);
                        }
                        finally {
                            entry.ExecutionLock.Release();
                        }
                    }
                ).ConfigureAwait(false);

                break;

            case ExecutionMode.Concurrent:
                await Task.WhenAll(
                    entries.Select(async entry => {
                            await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
                            try {
                                if (entry.IsDisconnected)
                                    return;

                                await entry.Receiver.HandleAsync(message, ct).ConfigureAwait(false);
                            }
                            finally {
                                entry.ExecutionLock.Release();
                            }
                        }
                    )
                ).ConfigureAwait(false);

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    #endregion

    #region Broadcast (Attributed Responses)

    /// <inheritdoc />
    public ValueTask<ReceiverReply<TResponse>[]> BroadcastAsync<T, TResponse>(
        T message, ExecutionMode? mode = null, CancellationToken ct = default
    ) {
        var entries            = _registry.GetReceivers<T, TResponse>();
        var simpleBehaviors    = _registry.GetBehaviors<T, TResponse>();
        var broadcastBehaviors = _registry.GetBroadcastBehaviors<T, TResponse>();

        if (entries.Length == 0)
            return ValueTask.FromResult(Array.Empty<ReceiverReply<TResponse>>());

        var executionMode = mode ?? _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && simpleBehaviors.Length == 0 && broadcastBehaviors.Length == 0) {
            var item = Directive.Create(
                (message, entries, executionMode),
                static async (state, ct) =>
                    await ExecuteEntriesAsync(state.entries, state.message, state.executionMode, ct)
                        .ConfigureAwait(false)
            );

            var writeTask = _channel.Writer.WriteAsync(item, ct);
            return writeTask.IsCompletedSuccessfully
                ? item.Task
                : AwaitWriteAndReturn(writeTask, item);
        }

        // Behaviors-only path (no middleware)
        if (_middleware.Length == 0) {
            // Prefer broadcast behaviors if available (full attribution)
            if (broadcastBehaviors.Length > 0) {
                var behaviorItem = Directive.Create(
                    (bus: this, message, entries, broadcastBehaviors, executionMode),
                    static async (state, ct) => {
                        var pipeline = state.bus.BuildBroadcastBehaviorPipeline(
                            state.broadcastBehaviors, state.entries, state.message, state.executionMode
                        );
                        return await pipeline(ct).ConfigureAwait(false);
                    }
                );

                var behaviorWriteTask = _channel.Writer.WriteAsync(behaviorItem, ct);
                return behaviorWriteTask.IsCompletedSuccessfully
                    ? behaviorItem.Task
                    : AwaitWriteAndReturn(behaviorWriteTask, behaviorItem);
            }

            // Use simple behaviors
            var simpleItem = Directive.Create(
                (bus: this, message, entries, simpleBehaviors, executionMode),
                static async (state, ct) => {
                    var pipeline = state.bus.BuildSimpleBehaviorPipeline(
                        state.simpleBehaviors, state.entries, state.message, state.executionMode
                    );
                    var response = await pipeline(ct).ConfigureAwait(false);
                    return WrapResponse(response, typeof(ITalynBehavior<T, TResponse>));
                }
            );

            var simpleWriteTask = _channel.Writer.WriteAsync(simpleItem, ct);
            return simpleWriteTask.IsCompletedSuccessfully
                ? simpleItem.Task
                : AwaitWriteAndReturn(simpleWriteTask, simpleItem);
        }

        // Full path: middleware + behaviors
        var middlewareItem = Directive.Create(
            (bus: this, message, entries, simpleBehaviors, broadcastBehaviors, executionMode),
            static async (state, ct) =>
                await state.bus.ExecuteWithBehaviorsAndMiddlewareAsync(
                    state.simpleBehaviors, state.broadcastBehaviors, state.entries,
                    state.message, state.executionMode, ct
                ).ConfigureAwait(false)
        );

        var middlewareWriteTask = _channel.Writer.WriteAsync(middlewareItem, ct);
        return middlewareWriteTask.IsCompletedSuccessfully
            ? middlewareItem.Task
            : AwaitWriteAndReturn(middlewareWriteTask, middlewareItem);

        static async ValueTask<ReceiverReply<TResponse>[]> AwaitWriteAndReturn(
            ValueTask writeTask,
            PooledDirective<ReceiverReply<TResponse>[]> item
        ) {
            await writeTask.ConfigureAwait(false);
            return await item.Task.ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public ValueTask BroadcastAsync<T, TResponse>(
        T message,
        OnOutcome<ReceiverReply<TResponse>[]> callback,
        ExecutionMode? mode = null,
        CancellationToken ct = default
    ) {
        var entries            = _registry.GetReceivers<T, TResponse>();
        var simpleBehaviors    = _registry.GetBehaviors<T, TResponse>();
        var broadcastBehaviors = _registry.GetBroadcastBehaviors<T, TResponse>();

        if (entries.Length == 0) {
            var callbackTask = callback(Outcome<ReceiverReply<TResponse>[]>.Success([]));
            return callbackTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : callbackTask;
        }

        var executionMode = mode ?? _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && simpleBehaviors.Length == 0 && broadcastBehaviors.Length == 0) {
            var item = Directive.Create(
                (message, entries, callback, executionMode),
                static async (state, ct) => {
                    try {
                        var results = await ExecuteEntriesAsync(
                                state.entries, state.message, state.executionMode, ct)
                            .ConfigureAwait(false);
                        await state.callback(Outcome<ReceiverReply<TResponse>[]>.Success(results)).ConfigureAwait(false);
                        return results;
                    }
                    catch (Exception ex) {
                        await state.callback(Outcome<ReceiverReply<TResponse>[]>.Failure(ex)).ConfigureAwait(false);
                        throw;
                    }
                }
            );

            var writeTask = _channel.Writer.WriteAsync(item, ct);
            return writeTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : AwaitWrite(writeTask);
        }

        // Behaviors-only path (no middleware)
        if (_middleware.Length == 0) {
            // Prefer broadcast behaviors if available
            if (broadcastBehaviors.Length > 0) {
                var behaviorItem = Directive.Create(
                    (bus: this, message, entries, broadcastBehaviors, callback, executionMode),
                    static async (state, ct) => {
                        try {
                            var pipeline = state.bus.BuildBroadcastBehaviorPipeline(
                                state.broadcastBehaviors, state.entries, state.message, state.executionMode
                            );
                            var results = await pipeline(ct).ConfigureAwait(false);
                            await state.callback(Outcome<ReceiverReply<TResponse>[]>.Success(results)).ConfigureAwait(false);
                            return results;
                        }
                        catch (Exception ex) {
                            await state.callback(Outcome<ReceiverReply<TResponse>[]>.Failure(ex)).ConfigureAwait(false);
                            throw;
                        }
                    }
                );

                var behaviorWriteTask = _channel.Writer.WriteAsync(behaviorItem, ct);
                return behaviorWriteTask.IsCompletedSuccessfully
                    ? ValueTask.CompletedTask
                    : AwaitWrite(behaviorWriteTask);
            }

            // Use simple behaviors
            var simpleItem = Directive.Create(
                (bus: this, message, entries, simpleBehaviors, callback, executionMode),
                static async (state, ct) => {
                    try {
                        var pipeline = state.bus.BuildSimpleBehaviorPipeline(
                            state.simpleBehaviors, state.entries, state.message, state.executionMode
                        );
                        var response = await pipeline(ct).ConfigureAwait(false);
                        var results = WrapResponse(response, typeof(ITalynBehavior<T, TResponse>));
                        await state.callback(Outcome<ReceiverReply<TResponse>[]>.Success(results)).ConfigureAwait(false);
                        return results;
                    }
                    catch (Exception ex) {
                        await state.callback(Outcome<ReceiverReply<TResponse>[]>.Failure(ex)).ConfigureAwait(false);
                        throw;
                    }
                }
            );

            var simpleWriteTask = _channel.Writer.WriteAsync(simpleItem, ct);
            return simpleWriteTask.IsCompletedSuccessfully
                ? ValueTask.CompletedTask
                : AwaitWrite(simpleWriteTask);
        }

        // Full path: middleware + behaviors
        var middlewareItem = Directive.Create(
            (bus: this, message, entries, simpleBehaviors, broadcastBehaviors, callback, executionMode),
            static async (state, ct) => {
                try {
                    var results = await state.bus.ExecuteWithBehaviorsAndMiddlewareAsync(
                            state.simpleBehaviors, state.broadcastBehaviors, state.entries,
                            state.message, state.executionMode, ct)
                        .ConfigureAwait(false);
                    await state.callback(Outcome<ReceiverReply<TResponse>[]>.Success(results)).ConfigureAwait(false);
                    return results;
                }
                catch (Exception ex) {
                    await state.callback(Outcome<ReceiverReply<TResponse>[]>.Failure(ex)).ConfigureAwait(false);
                    throw;
                }
            }
        );

        var middlewareWriteTask = _channel.Writer.WriteAsync(middlewareItem, ct);
        return middlewareWriteTask.IsCompletedSuccessfully
            ? ValueTask.CompletedTask
            : AwaitWrite(middlewareWriteTask);

        static async ValueTask AwaitWrite(ValueTask writeTask) {
            await writeTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Executes receiver entries with lock-check pattern and returns attributed responses.
    /// </summary>
    static async ValueTask<ReceiverReply<TResponse>[]> ExecuteEntriesAsync<T, TResponse>(
        ReceiverEntry<T, TResponse>[] entries,
        T message,
        ExecutionMode mode,
        CancellationToken ct
    ) {
        if (entries.Length == 0)
            return [];

        return mode switch {
            ExecutionMode.Sequential => await ExecuteSequentialAsync(entries, message, ct).ConfigureAwait(false),
            ExecutionMode.Parallel   => await ExecuteParallelAsync(entries, message, ct).ConfigureAwait(false),
            ExecutionMode.Concurrent => await ExecuteConcurrentAsync(entries, message, ct).ConfigureAwait(false),
            _                        => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    static async ValueTask<ReceiverReply<TResponse>[]> ExecuteSequentialAsync<T, TResponse>(
        ReceiverEntry<T, TResponse>[] entries, T message, CancellationToken ct
    ) {
        var results = new List<ReceiverReply<TResponse>>(entries.Length);

        foreach (var entry in entries) {
            await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
            try {
                if (entry.IsDisconnected)
                    continue;

                results.Add(await entry.Receiver.Process(message, ct).ConfigureAwait(false));
            }
            finally {
                entry.ExecutionLock.Release();
            }
        }

        return [..results];
    }

    static async ValueTask<ReceiverReply<TResponse>[]> ExecuteParallelAsync<T, TResponse>(
        ReceiverEntry<T, TResponse>[] entries, T message, CancellationToken ct
    ) {
        var lockResults = new ReceiverReply<TResponse>?[entries.Length];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, entries.Length), ct,
            async (i, token) => {
                var entry = entries[i];
                await entry.ExecutionLock.WaitAsync(token).ConfigureAwait(false);
                try {
                    if (entry.IsDisconnected)
                        return;

                    lockResults[i] = await entry.Receiver.Process(message, token).ConfigureAwait(false);
                }
                finally {
                    entry.ExecutionLock.Release();
                }
            }
        ).ConfigureAwait(false);

        return lockResults.Where(static r => r.HasValue).Select(static r => r!.Value).ToArray();
    }

    static async ValueTask<ReceiverReply<TResponse>[]> ExecuteConcurrentAsync<T, TResponse>(
        ReceiverEntry<T, TResponse>[] entries, T message, CancellationToken ct
    ) {
        var tasks = entries.Select(async entry => {
                await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
                try {
                    if (entry.IsDisconnected)
                        return (ReceiverReply<TResponse>?)null;

                    return await entry.Receiver.Process(message, ct).ConfigureAwait(false);
                }
                finally {
                    entry.ExecutionLock.Release();
                }
            }
        ).ToArray();

        var taskResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        return taskResults.Where(static r => r.HasValue).Select(static r => r!.Value).ToArray();
    }

    #endregion

    #region Invoke (Direct - Message Receivers)

    /// <inheritdoc />
    public ValueTask InvokeAsync<T>(T message, CancellationToken ct = default) {
        var entries   = _registry.GetReceivers<T>();
        var behaviors = _registry.GetBehaviors<T>();

        if (entries.Length == 0)
            return ValueTask.CompletedTask;

        var mode = _options.DefaultExecutionMode;

        // Fast path: no middleware and no behaviors
        if (_middleware.Length == 0 && behaviors.Length == 0) {
            if (entries.Length == 1 && !entries[0].IsDisconnected)
                return InvokeSingleEntryAsync(entries[0], message, ct);

            return InvokeEntriesAsync(entries, message, mode, ct);
        }

        // Behaviors-only path
        if (_middleware.Length == 0) {
            var pipeline = BuildBehaviorPipeline(behaviors, entries, message, mode);
            return pipeline(ct);
        }

        // Full path: middleware + behaviors
        return InvokeWithBehaviorsAndMiddlewareAsync(behaviors, entries, message, mode, DispatchKind.Transmit, ct);
    }

    static async ValueTask InvokeSingleEntryAsync<T>(ReceiverEntry<T> entry, T message, CancellationToken ct) {
        await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            if (!entry.IsDisconnected)
                await entry.Receiver.HandleAsync(message, ct).ConfigureAwait(false);
        }
        finally {
            entry.ExecutionLock.Release();
        }
    }

    #endregion

    #region Connect/Disconnect

    /// <inheritdoc />
    public ITalynBus Connect<T>(IReceiver<T> receiver) {
        _registry.Connect(receiver);
        return this;
    }

    /// <inheritdoc />
    public ITalynBus Disconnect<T>(IReceiver<T> receiver) {
        _registry.Disconnect(receiver);
        return this;
    }

    /// <inheritdoc />
    public async ValueTask<ITalynBus> DisconnectAsync<T>(IReceiver<T> receiver, CancellationToken ct = default) {
        var entry = _registry.Disconnect(receiver);
        if (entry is not null) {
            // Wait for any active execution to complete
            await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
            entry.ExecutionLock.Release();
        }

        return this;
    }

    /// <inheritdoc />
    public ITalynBus Connect<T, TResponse>(IReceiver<T, TResponse> receiver) {
        _registry.Connect(receiver);
        return this;
    }

    /// <inheritdoc />
    public ITalynBus Disconnect<T, TResponse>(IReceiver<T, TResponse> receiver) {
        _registry.Disconnect(receiver);
        return this;
    }

    /// <inheritdoc />
    public async ValueTask<ITalynBus> DisconnectAsync<T, TResponse>(
        IReceiver<T, TResponse> receiver, CancellationToken ct = default
    ) {
        var entry = _registry.Disconnect(receiver);
        if (entry is not null) {
            // Wait for any active execution to complete
            await entry.ExecutionLock.WaitAsync(ct).ConfigureAwait(false);
            entry.ExecutionLock.Release();
        }

        return this;
    }

    #endregion
}

#endregion

#region Internal Extensions

/// <summary>
/// Internal extension methods for processing receivers with timing and error capture.
/// </summary>
static class RegistryExtensions {
    extension<T, TResponse>(IReceiver<T, TResponse> receiver) {
        public async ValueTask<ReceiverReply<TResponse>> Process(T message, CancellationToken ct) {
            var start        = TimeProvider.System.GetTimestamp();
            var receiverType = receiver.GetType();

            try {
                var response = await receiver.HandleAsync(message, ct).ConfigureAwait(false);
                return ReceiverReply<TResponse>.FromResult(response, receiverType, start);
            }
            catch (Exception ex) {
                return ReceiverReply<TResponse>.FromError(ex, receiverType, start);
            }
        }
    }

    extension<T>(IReceiver<T> receiver) {
        public async ValueTask<ReceiverReply<bool>> Process(T message, CancellationToken ct) {
            var start        = TimeProvider.System.GetTimestamp();
            var receiverType = receiver.GetType();

            try {
                await receiver.HandleAsync(message, ct).ConfigureAwait(false);
                return ReceiverReply<bool>.FromResult(true, receiverType, start);
            }
            catch (Exception ex) {
                return ReceiverReply<bool>.FromError(ex, receiverType, start);
            }
        }
    }
}

#endregion

#region Bus Extensions

/// <summary>
/// Convenience extension methods for single-receiver request/response operations.
/// </summary>
/// <remarks>
/// These methods wrap <see cref="ITalynBus.BroadcastAsync{T,TResponse}(T,ExecutionMode?,CancellationToken)"/>
/// and <see cref="ITalynMediator.ScatterAsync{T,TResponse}"/> for the common case
/// where exactly one receiver is expected. They throw <see cref="InvalidOperationException"/>
/// if zero or multiple receivers are registered.
/// </remarks>
public static class TalynBusExtensions {
    /// <summary>
    /// Invokes a request on a single <see cref="IReceiver{T,TResponse}"/> receiver directly,
    /// bypassing the channel queue.
    /// </summary>
    /// <typeparam name="T">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="mediator">The mediator instance.</param>
    /// <param name="message">The request message.</param>
    /// <param name="ct">A cancellation token to cancel the operation.</param>
    /// <returns>The response from the single registered receiver.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when zero receivers or more than one receiver is registered for type <typeparamref name="T"/>.
    /// </exception>
    public static async ValueTask<TResponse> InvokeAsync<T, TResponse>(
        this ITalynMediator mediator, T message, CancellationToken ct = default
    ) {
        var replies = await mediator.ScatterAsync<T, TResponse>(message, ct: ct).ConfigureAwait(false);
        return UnwrapSingle<T, TResponse>(replies);
    }

    static TResponse UnwrapSingle<T, TResponse>(ReceiverReply<TResponse>[] replies) =>
        replies.Length switch {
            0 => throw new InvalidOperationException($"No receiver registered for {typeof(T).Name}"),
            1 => replies[0].Result.GetValueOrThrow(),
            _ => throw new InvalidOperationException($"Expected single receiver for {typeof(T).Name}, found {replies.Length}")
        };

    static Outcome<TResponse> UnwrapSingleOutcome<T, TResponse>(Outcome<ReceiverReply<TResponse>[]> outcome) {
        if (!outcome.IsSuccess)
            return Outcome<TResponse>.Failure(outcome.Error);

        var replies = outcome.Response;
        return replies.Length switch {
            0 => Outcome<TResponse>.Failure(new InvalidOperationException($"No receiver registered for {typeof(T).Name}")),
            1 => replies[0].Result,
            _ => Outcome<TResponse>.Failure(new InvalidOperationException($"Expected single receiver for {typeof(T).Name}, found {replies.Length}"))
        };
    }

    extension(ITalynBus bus) {
        /// <summary>
        /// Transmits a request to a single <see cref="IReceiver{T,TResponse}"/> receiver through the queue.
        /// </summary>
        /// <typeparam name="T">The request message type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="message">The request message.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>The response from the single registered receiver.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when zero receivers or more than one receiver is registered for type <typeparamref name="T"/>.
        /// </exception>
        public async ValueTask<TResponse> TransmitAsync<T, TResponse>(T message, CancellationToken ct = default) {
            var replies = await bus.BroadcastAsync<T, TResponse>(message, ct: ct).ConfigureAwait(false);
            return UnwrapSingle<T, TResponse>(replies);
        }

        /// <summary>
        /// Transmits a request to a single <see cref="IReceiver{T,TResponse}"/> receiver through the queue,
        /// invoking a callback when complete. Fire-and-forget semantics.
        /// </summary>
        /// <typeparam name="T">The request message type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="message">The request message.</param>
        /// <param name="callback">A callback invoked with the response or an error.</param>
        /// <param name="ct">A cancellation token to cancel the operation.</param>
        /// <returns>A task that completes when the message is queued.</returns>
        public ValueTask TransmitAsync<T, TResponse>(T message, OnOutcome<TResponse> callback, CancellationToken ct = default) =>
            bus.BroadcastAsync<T, TResponse>(
                message, async outcome => await callback(UnwrapSingleOutcome<T, TResponse>(outcome)).ConfigureAwait(false), ct: ct
            );
    }
}

#endregion

#region Internals

delegate ValueTask<T> Computation<in TState, T>(TState state, CancellationToken ct);

interface IDirective : IDisposable {
    ValueTask ExecuteAsync(CancellationToken ct);
}

/// <summary>
/// Cached executor — one per type combo, never allocates after first use.
/// </summary>
static class Executor<TState, T> {
    public static readonly Func<object, object?, CancellationToken, ValueTask<T>> Invoke =
        static (computation, state, ct) =>
            Unsafe.As<Computation<TState, T>>(computation)((TState)state!, ct);
}

sealed class PooledDirective<T> : IDirective, IValueTaskSource<T> {
    readonly ObjectPool<PooledDirective<T>> _pool;
    object?                                 _computation;
    ManualResetValueTaskSourceCore<T>       _core;

    #if DEBUG
    string? _creationStack;
    #endif
    Func<object, object?, CancellationToken, ValueTask<T>>? _executor;
    int                                                     _returned;

    object? _state;

    internal PooledDirective(ObjectPool<PooledDirective<T>> pool) => _pool = pool;

    public ValueTask<T> Task => new(this, _core.Version);

    public ValueTask ExecuteAsync(CancellationToken ct) {
        try {
            var task = _executor!(_computation!, _state, ct);

            if (task.IsCompletedSuccessfully) {
                _core.SetResult(task.Result);
                return ValueTask.CompletedTask;
            }

            return AwaitAndComplete(task);
        }
        catch (Exception ex) {
            _core.SetException(ex);
            return ValueTask.CompletedTask;
        }

        async ValueTask AwaitAndComplete(ValueTask<T> task) {
            try {
                var result = await task.ConfigureAwait(false);
                _core.SetResult(result);
            }
            catch (Exception ex) {
                _core.SetException(ex);
            }
        }
    }

    [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize")]
    public void Dispose() => Return();

    // IValueTaskSource<T>
    public T GetResult(short token) {
        var result = _core.GetResult(token);
        Return();
        return result;
    }

    public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags) =>
        _core.OnCompleted(
            continuation, state, token,
            flags
        );

    internal void Configure<TState>(TState state, Computation<TState, T> computation) {
        _core.Reset();

        _state       = state;
        _computation = computation;
        _executor    = Executor<TState, T>.Invoke;
        _returned    = 0;

        #if DEBUG
        _creationStack = Environment.StackTrace;
        #endif
        GC.ReRegisterForFinalize(this);
    }

    void Return() {
        if (Interlocked.Exchange(ref _returned, 1) != 0)
            return;

        GC.SuppressFinalize(this);

        _state       = null;
        _computation = null;
        _executor    = null;
        _pool.Return(this);
    }

    ~PooledDirective() {
        if (Environment.HasShutdownStarted)
            return;

        #if DEBUG
        System.Diagnostics.Debug.Fail($"Directive<{typeof(T).Name}> was leaked. Created at:{Environment.NewLine}{_creationStack}");
        #endif
        Return();
    }
}

sealed class DirectivePoolPolicy<T> : IPooledObjectPolicy<PooledDirective<T>> {
    ObjectPool<PooledDirective<T>>? _pool;

    public PooledDirective<T> Create() => new(_pool!);

    public bool Return(PooledDirective<T> obj) => true;

    internal void SetPool(ObjectPool<PooledDirective<T>> pool) => _pool = pool;
}

static class Directive {
    public static PooledDirective<T> Create<T, TState>(TState state, Computation<TState, T> computation) {
        var item = DirectiveCache<T>.Pool.Get();
        item.Configure(state, computation);
        return item;
    }

    static class DirectiveCache<T> {
        internal static readonly ObjectPool<PooledDirective<T>> Pool;

        static DirectiveCache() {
            var policy = new DirectivePoolPolicy<T>();
            Pool = new DefaultObjectPool<PooledDirective<T>>(policy, Environment.ProcessorCount * 2);
            policy.SetPool(Pool);
        }
    }
}

#endregion

#region Logging

public sealed partial class TalynBus {
    static partial class Log {
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Consumer started [ChannelCapacity={ChannelCapacity}, FailFast={FailFast}, DrainOnDispose={DrainOnDispose}]"
        )]
        public static partial void ConsumerStarted(
            ILogger logger,
            int channelCapacity,
            bool failFast,
            bool drainOnDispose
        );

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Consumer stopped [ProcessedCount={ProcessedCount}]"
        )]
        public static partial void ConsumerStopped(ILogger logger, long processedCount);

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Directive cancelled [ProcessedCount={ProcessedCount}]"
        )]
        public static partial void DirectiveCancelled(ILogger logger, long processedCount);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Directive failed [ProcessedCount={ProcessedCount}]"
        )]
        public static partial void DirectiveFailed(ILogger logger, Exception ex, long processedCount);

        [LoggerMessage(
            Level = LogLevel.Critical,
            Message = "Consumer failed [ProcessedCount={ProcessedCount}]"
        )]
        public static partial void ConsumerFailed(ILogger logger, Exception ex, long processedCount);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Disposing [DrainOnDispose={DrainOnDispose}]"
        )]
        public static partial void Disposing(ILogger logger, bool drainOnDispose);

        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Cancellation requested"
        )]
        public static partial void CancellationRequested(ILogger logger);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Disposed"
        )]
        public static partial void Disposed(ILogger logger);
    }
}

#endregion
