using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Talyn;

/// <summary>
/// Builder for configuring Talyn bus options and registering receivers with DI.
/// </summary>
/// <remarks>
/// <para>
/// This builder is used exclusively with <see cref="ServiceCollectionExtensions.AddTalyn"/>
/// for DI integration. For standalone scenarios without DI, use <see cref="TalynBus.Create"/> instead.
/// </para>
/// </remarks>
public sealed class TalynBuilder {
    readonly IServiceCollection                              _services;
    readonly List<Func<IServiceProvider, ITalynMiddleware>> _middlewareFactories = new();

    TalynOptions _options = new();

    /// <summary>
    /// Creates a builder that integrates with an existing DI container.
    /// </summary>
    /// <param name="services">The service collection to register receivers into.</param>
    internal TalynBuilder(IServiceCollection services) => _services = services;

    /// <summary>
    /// The underlying service collection for registering receivers.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    /// Internal accessor for the configured options (used by AddTalyn factory).
    /// </summary>
    internal TalynOptions Options => _options;

    /// <summary>
    /// Configures the bus with the specified options instance.
    /// </summary>
    /// <param name="options">The options to use.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithOptions(TalynOptions options) {
        _options = options;
        return this;
    }

    /// <summary>
    /// Configures the bus options using an action.
    /// </summary>
    /// <param name="configure">The action to configure options.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithOptions(Action<TalynOptions> configure) {
        configure(_options);
        return this;
    }

    #region Middleware Registration

    /// <summary>
    /// Registers a middleware type to be resolved from DI.
    /// </summary>
    /// <typeparam name="TMiddleware">The middleware implementation type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// The middleware is registered as a singleton if not already registered.
    /// Middleware executes in registration order (first registered = outermost wrapper).
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .UseMiddleware&lt;TracingMiddleware&gt;()
    ///     .UseMiddleware&lt;MetricsMiddleware&gt;());
    /// </code>
    /// </example>
    public TalynBuilder UseMiddleware<TMiddleware>() where TMiddleware : class, ITalynMiddleware {
        Services.TryAddSingleton<TMiddleware>();
        _middlewareFactories.Add(sp => sp.GetRequiredService<TMiddleware>());
        return this;
    }

    /// <summary>
    /// Registers a middleware instance.
    /// </summary>
    /// <param name="middleware">The middleware instance.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Middleware executes in registration order (first registered = outermost wrapper).
    /// </remarks>
    public TalynBuilder UseMiddleware(ITalynMiddleware middleware) {
        _middlewareFactories.Add(_ => middleware);
        return this;
    }

    /// <summary>
    /// Registers a middleware using a factory function.
    /// </summary>
    /// <param name="factory">Factory to create the middleware from the service provider.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Middleware executes in registration order (first registered = outermost wrapper).
    /// </remarks>
    public TalynBuilder UseMiddleware(Func<IServiceProvider, ITalynMiddleware> factory) {
        _middlewareFactories.Add(factory);
        return this;
    }

    /// <summary>
    /// Registers an inline middleware using a lambda delegate.
    /// </summary>
    /// <param name="handler">The middleware delegate.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This is a convenience method for simple middleware that doesn't need a dedicated class.
    /// Middleware executes in registration order (first registered = outermost wrapper).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .Use(async (ctx, next) => {
    ///         var sw = Stopwatch.GetTimestamp();
    ///         await next(ctx);
    ///         Console.WriteLine($"Dispatched {ctx.MessageType.Name} in {Stopwatch.GetElapsedTime(sw)}");
    ///     }));
    /// </code>
    /// </example>
    public TalynBuilder Use(Func<TalynMessageContext, TalynMiddlewareDelegate, ValueTask> handler) {
        _middlewareFactories.Add(_ => new LambdaMiddleware(handler));
        return this;
    }

    /// <summary>
    /// Builds the middleware array from the registered factories.
    /// </summary>
    /// <param name="sp">The service provider for resolving middleware instances.</param>
    /// <returns>Array of middleware instances, or null if none registered.</returns>
    internal ITalynMiddleware[]? BuildMiddleware(IServiceProvider sp) =>
        _middlewareFactories.Count > 0
            ? _middlewareFactories.Select(f => f(sp)).ToArray()
            : null;

    #endregion

    /// <summary>
    /// Registers a receiver by discovering its interface types via reflection.
    /// Supports both <see cref="IReceiver{T}"/> and <see cref="IReceiver{T,TResponse}"/>.
    /// </summary>
    /// <typeparam name="TReceiver">The receiver implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <exception cref="ArgumentException">Thrown when TReceiver doesn't implement a receiver interface.</exception>
    public TalynBuilder WithReceiver<TReceiver>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TReceiver : class {
        var receiverType = typeof(TReceiver);
        var registered   = false;

        foreach (var iface in receiverType.GetInterfaces()) {
            if (!iface.IsGenericType)
                continue;

            var genericDef = iface.GetGenericTypeDefinition();

            // IReceiver<T, TResponse>
            if (genericDef == typeof(IReceiver<,>)) {
                Services.Add(new ServiceDescriptor(iface, receiverType, lifetime));
                registered = true;
            }
            // IReceiver<TMessage>
            else if (genericDef == typeof(IReceiver<>)) {
                Services.Add(new ServiceDescriptor(iface, receiverType, lifetime));
                registered = true;
            }
        }

        if (!registered)
            throw new ArgumentException(
                $"{receiverType.Name} must implement IReceiver<T> or IReceiver<T, TResponse>",
                nameof(TReceiver)
            );

        return this;
    }

    /// <summary>
    /// Registers a message receiver for Transmit operations.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TReceiver">The receiver implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<TMessage, TReceiver>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TReceiver : class, IReceiver<TMessage> {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<TMessage>), typeof(TReceiver), lifetime));
        return this;
    }

    /// <summary>
    /// Registers a request receiver for Request/Response and Broadcast operations.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TReceiver">The receiver implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TResponse, TReceiver>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TReceiver : class, IReceiver<T, TResponse> {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<T, TResponse>), typeof(TReceiver), lifetime));
        return this;
    }

    /// <summary>
    /// Registers a receiver instance. Types are inferred from the instance.
    /// </summary>
    /// <typeparam name="T">The request type (inferred).</typeparam>
    /// <typeparam name="TResponse">The response type (inferred).</typeparam>
    /// <param name="instance">The receiver instance.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// When an instance is provided, <see cref="ServiceLifetime.Singleton"/> uses the instance directly.
    /// For <see cref="ServiceLifetime.Scoped"/> or <see cref="ServiceLifetime.Transient"/>,
    /// the same instance is returned (effectively singleton behavior).
    /// Use the factory overload for true scoped/transient semantics.
    /// </remarks>
    public TalynBuilder WithReceiver<T, TResponse>(IReceiver<T, TResponse> instance, ServiceLifetime lifetime = ServiceLifetime.Singleton) {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<T, TResponse>), _ => instance, lifetime));
        return this;
    }

    /// <summary>
    /// Registers a message receiver instance. Type is inferred from the instance.
    /// </summary>
    /// <typeparam name="TMessage">The message type (inferred).</typeparam>
    /// <param name="instance">The receiver instance.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// When an instance is provided, <see cref="ServiceLifetime.Singleton"/> uses the instance directly.
    /// For <see cref="ServiceLifetime.Scoped"/> or <see cref="ServiceLifetime.Transient"/>,
    /// the same instance is returned (effectively singleton behavior).
    /// Use the factory overload for true scoped/transient semantics.
    /// </remarks>
    public TalynBuilder WithReceiver<TMessage>(IReceiver<TMessage> instance, ServiceLifetime lifetime = ServiceLifetime.Singleton) {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<TMessage>), _ => instance, lifetime));
        return this;
    }

    /// <summary>
    /// Registers a receiver using a factory. Types are inferred from the factory return type.
    /// </summary>
    /// <typeparam name="T">The request type (inferred).</typeparam>
    /// <typeparam name="TResponse">The response type (inferred).</typeparam>
    /// <param name="factory">Factory to create the receiver.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TResponse>(
        Func<IServiceProvider, IReceiver<T, TResponse>> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton
    ) {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<T, TResponse>), factory, lifetime));
        return this;
    }

    /// <summary>
    /// Registers a message receiver using a factory. Type is inferred from the factory return type.
    /// </summary>
    /// <typeparam name="TMessage">The message type (inferred).</typeparam>
    /// <param name="factory">Factory to create the receiver.</param>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<TMessage>(Func<IServiceProvider, IReceiver<TMessage>> factory, ServiceLifetime lifetime = ServiceLifetime.Singleton) {
        Services.Add(new ServiceDescriptor(typeof(IReceiver<TMessage>), factory, lifetime));
        return this;
    }

    #region Behavior Registration

    /// <summary>
    /// Registers a behavior type for fire-and-forget messages to be resolved from DI.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <typeparam name="TBehavior">The behavior implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// Behaviors execute in registration order, wrapping the receivers.
    /// First registered = outermost wrapper.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .UseBehavior&lt;OrderPlaced, LoggingBehavior&lt;OrderPlaced&gt;&gt;());
    /// </code>
    /// </example>
    public TalynBuilder UseBehavior<TMessage, TBehavior>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TBehavior : class, ITalynBehavior<TMessage> {
        Services.Add(new ServiceDescriptor(typeof(ITalynBehavior<TMessage>), typeof(TBehavior), lifetime));
        return this;
    }

    /// <summary>
    /// Registers a behavior instance for fire-and-forget messages.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <param name="behavior">The behavior instance.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder UseBehavior<TMessage>(ITalynBehavior<TMessage> behavior) {
        Services.AddSingleton(behavior);
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
    /// services.AddTalyn(bus => bus
    ///     .UseBehavior&lt;OrderPlaced&gt;(async (msg, next, ct) => {
    ///         Console.WriteLine($"Before: {msg}");
    ///         await next(ct);
    ///         Console.WriteLine($"After: {msg}");
    ///     }));
    /// </code>
    /// </example>
    public TalynBuilder UseBehavior<TMessage>(
        Func<TMessage, BehaviorDelegate, CancellationToken, ValueTask> handler
    ) {
        Services.AddSingleton<ITalynBehavior<TMessage>>(new LambdaBehavior<TMessage>(handler));
        return this;
    }

    /// <summary>
    /// Registers a behavior type for request/response messages to be resolved from DI.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TBehavior">The behavior implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This behavior works with the first successful response from receivers.
    /// Use <see cref="UseBroadcastBehavior{TMessage,TResponse,TBehavior}"/> when you need
    /// access to all receiver responses with attribution.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .UseBehavior&lt;GetOrder, Order, ValidationBehavior&lt;GetOrder, Order&gt;&gt;());
    /// </code>
    /// </example>
    public TalynBuilder UseBehavior<TMessage, TResponse, TBehavior>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TBehavior : class, ITalynBehavior<TMessage, TResponse> {
        Services.Add(new ServiceDescriptor(typeof(ITalynBehavior<TMessage, TResponse>), typeof(TBehavior), lifetime));
        return this;
    }

    /// <summary>
    /// Registers a behavior instance for request/response messages.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="behavior">The behavior instance.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder UseBehavior<TMessage, TResponse>(ITalynBehavior<TMessage, TResponse> behavior) {
        Services.AddSingleton(behavior);
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
    /// services.AddTalyn(bus => bus
    ///     .UseBehavior&lt;GetOrder, Order&gt;(async (msg, next, ct) => {
    ///         Console.WriteLine($"Before: {msg.Id}");
    ///         var response = await next(ct);
    ///         Console.WriteLine($"After: {response.Total}");
    ///         return response;
    ///     }));
    /// </code>
    /// </example>
    public TalynBuilder UseBehavior<TMessage, TResponse>(
        Func<TMessage, BehaviorDelegate<TResponse>, CancellationToken, ValueTask<TResponse>> handler
    ) {
        Services.AddSingleton<ITalynBehavior<TMessage, TResponse>>(new LambdaBehavior<TMessage, TResponse>(handler));
        return this;
    }

    /// <summary>
    /// Registers a broadcast behavior type for request/response messages to be resolved from DI.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TBehavior">The behavior implementation type.</typeparam>
    /// <param name="lifetime">The service lifetime. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Broadcast behaviors have full access to all receiver responses with attribution,
    /// including timing data, error handling per-receiver, and response aggregation.
    /// Use <see cref="UseBehavior{TMessage,TResponse,TBehavior}"/> for simpler scenarios
    /// where you only need the first successful response.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .UseBroadcastBehavior&lt;GetOrder, Order, MetricsBehavior&lt;GetOrder, Order&gt;&gt;());
    /// </code>
    /// </example>
    public TalynBuilder UseBroadcastBehavior<TMessage, TResponse, TBehavior>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TBehavior : class, ITalynBroadcastBehavior<TMessage, TResponse> {
        Services.Add(new ServiceDescriptor(typeof(ITalynBroadcastBehavior<TMessage, TResponse>), typeof(TBehavior), lifetime));
        return this;
    }

    /// <summary>
    /// Registers a broadcast behavior instance for request/response messages.
    /// </summary>
    /// <typeparam name="TMessage">The message type the behavior handles.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="behavior">The behavior instance.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder UseBroadcastBehavior<TMessage, TResponse>(ITalynBroadcastBehavior<TMessage, TResponse> behavior) {
        Services.AddSingleton(behavior);
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
    /// services.AddTalyn(bus => bus
    ///     .UseBroadcastBehavior&lt;GetOrder, Order&gt;(async (msg, next, ct) => {
    ///         var results = await next(ct);
    ///         foreach (var reply in results) {
    ///             Console.WriteLine($"Receiver {reply.ReceiverType.Name}: {reply.Duration}");
    ///         }
    ///         return results;
    ///     }));
    /// </code>
    /// </example>
    public TalynBuilder UseBroadcastBehavior<TMessage, TResponse>(
        Func<TMessage, BehaviorDelegate<ReceiverReply<TResponse>[]>, CancellationToken, ValueTask<ReceiverReply<TResponse>[]>> handler
    ) {
        Services.AddSingleton<ITalynBroadcastBehavior<TMessage, TResponse>>(new LambdaBroadcastBehavior<TMessage, TResponse>(handler));
        return this;
    }

    #endregion

    #region Lambda Handlers (Zero-Allocation with TState)

    /// <summary>
    /// Registers a lambda message handler with explicit state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state instance to pass to the handler.</param>
    /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var logger = loggerFactory.CreateLogger("Orders");
    /// services.AddTalyn(bus => bus
    ///     .WithReceiver&lt;OrderPlaced, ILogger&gt;(logger,
    ///         static async (log, msg, ct) => log.LogInformation("Order {Id}", msg.Id)
    ///     ));
    /// </code>
    /// </example>
    public TalynBuilder WithReceiver<T, TState>(TState state, MessageHandler<TState, T> handler) {
        Services.AddSingleton<IReceiver<T>>(new LambdaReceiver<T>((msg, ct) => handler(state, msg, ct)));
        return this;
    }

    /// <summary>
    /// Registers a lambda message handler without state.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="handler">The handler delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T>(MessageHandler<T> handler) {
        Services.AddSingleton<IReceiver<T>>(new LambdaReceiver<T>(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous lambda message handler with explicit state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state instance to pass to the handler.</param>
    /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TState>(TState state, SyncMessageHandler<TState, T> handler) {
        Services.AddSingleton<IReceiver<T>>(new LambdaReceiver<T>((msg, _) => {
            handler(state, msg);
            return ValueTask.CompletedTask;
        }));
        return this;
    }

    /// <summary>
    /// Registers a synchronous lambda message handler without state.
    /// </summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="handler">The synchronous handler delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T>(SyncMessageHandler<T> handler) {
        Services.AddSingleton<IReceiver<T>>(new LambdaReceiver<T>((msg, _) => {
            handler(msg);
            return ValueTask.CompletedTask;
        }));
        return this;
    }

    /// <summary>
    /// Registers a lambda request handler with explicit state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state instance to pass to the handler.</param>
    /// <param name="handler">The handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The builder for chaining.</returns>
    /// <example>
    /// <code>
    /// var repo = new OrderRepository();
    /// services.AddTalyn(bus => bus
    ///     .WithReceiver&lt;GetOrder, Order, OrderRepository&gt;(repo,
    ///         static async (r, query, ct) => await r.GetAsync(query.Id, ct)
    ///     ));
    /// </code>
    /// </example>
    public TalynBuilder WithReceiver<T, TResponse, TState>(TState state, RequestHandler<TState, T, TResponse> handler) {
        Services.AddSingleton<IReceiver<T, TResponse>>(new LambdaRequestReceiver<T, TResponse>((msg, ct) => handler(state, msg, ct)));
        return this;
    }

    /// <summary>
    /// Registers a lambda request handler without state.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The handler delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TResponse>(RequestHandler<T, TResponse> handler) {
        Services.AddSingleton<IReceiver<T, TResponse>>(new LambdaRequestReceiver<T, TResponse>(handler));
        return this;
    }

    /// <summary>
    /// Registers a synchronous lambda request handler with explicit state (zero-allocation pattern).
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <param name="state">The state instance to pass to the handler.</param>
    /// <param name="handler">The synchronous handler delegate. Use <c>static</c> to ensure no closure allocation.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TResponse, TState>(TState state, SyncRequestHandler<TState, T, TResponse> handler) {
        Services.AddSingleton<IReceiver<T, TResponse>>(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(state, msg))));
        return this;
    }

    /// <summary>
    /// Registers a synchronous lambda request handler without state.
    /// </summary>
    /// <typeparam name="T">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="handler">The synchronous handler delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public TalynBuilder WithReceiver<T, TResponse>(SyncRequestHandler<T, TResponse> handler) {
        Services.AddSingleton<IReceiver<T, TResponse>>(new LambdaRequestReceiver<T, TResponse>((msg, _) => ValueTask.FromResult(handler(msg))));
        return this;
    }

    #endregion
}

/// <summary>
/// Extension methods for registering Talyn bus services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the Talyn bus and receiver registry to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action for the builder.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="ITalynRegistry"/> as singleton (wraps the service provider)</item>
    ///   <item><see cref="ITalynMediator"/> as singleton (direct operations, bypasses channel)</item>
    ///   <item><see cref="ITalynBus"/> as singleton (queued operations with backpressure)</item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddTalyn(bus => bus
    ///     .WithReceiver&lt;MyMessage, MyReceiver&gt;()
    ///     .WithReceiver&lt;MyRequest, MyResponse, MyRequestReceiver&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddTalyn(
        this IServiceCollection services,
        Action<TalynBuilder>? configure = null
    ) {
        var builder = new TalynBuilder(services);
        configure?.Invoke(builder);

        services.TryAddSingleton<ITalynRegistry>(sp => {
                var receiverResolver = new TalynContainerResolver(sp);
                var behaviorResolver = new TalynContainerBehaviorResolver(sp);
                return new TalynRegistry([receiverResolver], [behaviorResolver]);
            }
        );

        services.TryAddSingleton<ITalynBus>(sp => {
                var registry   = sp.GetRequiredService<ITalynRegistry>();
                var logger     = sp.GetService<ILogger<TalynBus>>();
                var middleware = builder.BuildMiddleware(sp);
                return new TalynBus(registry, builder.Options, logger, middleware);
            }
        );

        // Register ITalynMediator as the same instance as ITalynBus
        services.TryAddSingleton<ITalynMediator>(sp => sp.GetRequiredService<ITalynBus>());

        return services;
    }
}
