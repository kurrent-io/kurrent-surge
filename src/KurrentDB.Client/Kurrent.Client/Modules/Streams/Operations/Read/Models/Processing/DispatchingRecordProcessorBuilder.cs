using Microsoft.Extensions.Logging;

namespace Kurrent.Client.Streams;

/// <summary>
/// Builder for creating a <see cref="DispatchingRecordProcessor"/> with registered type handlers.
/// </summary>
/// <remarks>
/// <para>
/// The builder creates and configures a <see cref="ServiceProviderRegistry"/> internally,
/// or uses an externally provided <see cref="IRecordProcessorRegistry"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Create with handlers
/// var processor = DispatchingRecordProcessor.Create()
///     .WithHandler&lt;OrderPlaced, OrderPlacedProcessor&gt;()
///     .WithHandler&lt;OrderShipped&gt;((value, ctx) =&gt; {
///         Console.WriteLine($"Shipped: {value.OrderId}");
///         return ValueTask.CompletedTask;
///     })
///     .OnUnhandledRecord(r =&gt; Console.WriteLine($"Unhandled: {r.ValueType}"))
///     .Build();
///
/// // Create with custom invoker
/// var processor = DispatchingRecordProcessor.Create()
///     .WithInvoker(new DynamicInvoker())
///     .WithHandler&lt;OrderPlaced, OrderPlacedProcessor&gt;()
///     .Build();
///
/// // Create with existing registry
/// var registry = new ServiceProviderRegistry();
/// registry.Register&lt;OrderPlaced, OrderPlacedProcessor&gt;();
/// var processor = DispatchingRecordProcessor.Create(registry).Build();
/// </code>
/// </example>
[PublicAPI]
public sealed class DispatchingRecordProcessorBuilder {
    IRecordProcessorRegistry? _registry;
    IRecordProcessorInvoker?  _invoker;
    ILoggerFactory?           _loggerFactory;
    Action<Record>?           _onUnhandled;

    /// <summary>
    /// Creates a builder with a new internal registry.
    /// </summary>
    internal DispatchingRecordProcessorBuilder() { }

    /// <summary>
    /// Creates a builder with an existing registry.
    /// </summary>
    /// <param name="registry">The registry to use.</param>
    internal DispatchingRecordProcessorBuilder(IRecordProcessorRegistry registry) => _registry = registry;

    /// <summary>
    /// Sets the invoker to use for dispatching to typed handlers.
    /// </summary>
    /// <param name="invoker">The invoker implementation.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// If not set, defaults to <see cref="CompiledExpressionInvoker"/>.
    /// Use this to swap implementations for benchmarking:
    /// <list type="bullet">
    ///   <item><see cref="CompiledExpressionInvoker"/> - Expression trees (default)</item>
    ///   <item><see cref="ReflectionInvoker"/> - Pure reflection</item>
    ///   <item><see cref="DynamicInvoker"/> - C# dynamic (DLR)</item>
    /// </list>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an external registry was provided.
    /// </exception>
    public DispatchingRecordProcessorBuilder WithInvoker(IRecordProcessorInvoker invoker) {
        EnsureNoExternalRegistry();
        _invoker = invoker;
        return this;
    }

    /// <summary>
    /// Sets the logger factory for the registry.
    /// </summary>
    /// <param name="loggerFactory">The logger factory to use.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an external registry was provided.
    /// </exception>
    public DispatchingRecordProcessorBuilder WithLoggerFactory(ILoggerFactory loggerFactory) {
        EnsureNoExternalRegistry();
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Sets a callback to be invoked when a record has no registered handler.
    /// </summary>
    /// <param name="callback">The callback to invoke for unhandled records.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an external registry was provided.
    /// </exception>
    public DispatchingRecordProcessorBuilder OnUnhandledRecord(Action<Record> callback) {
        EnsureNoExternalRegistry();
        _onUnhandled = callback;
        return this;
    }

    /// <summary>
    /// Registers a handler type for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <typeparam name="THandler">The handler implementation type.</typeparam>
    /// <returns>This builder instance for method chaining.</returns>
    public DispatchingRecordProcessorBuilder WithHandler<T, THandler>()
        where T : class
        where THandler : class, IRecordProcessor<T> {
        EnsureRegistry().Register<T, THandler>();
        return this;
    }

    /// <summary>
    /// Registers a handler instance for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="processor">The processor instance.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public DispatchingRecordProcessorBuilder WithHandler<T>(IRecordProcessor<T> processor) where T : class {
        EnsureRegistry().Register(processor);
        return this;
    }

    /// <summary>
    /// Registers a delegate handler for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="handler">The async delegate to invoke for each record.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public DispatchingRecordProcessorBuilder WithHandler<T>(Func<T, ProcessRecordsContext, ValueTask> handler) where T : class {
        EnsureRegistry().Register(handler);
        return this;
    }

    /// <summary>
    /// Registers a synchronous delegate handler for records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record value type to handle.</typeparam>
    /// <param name="handler">The delegate to invoke for each record.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public DispatchingRecordProcessorBuilder WithHandler<T>(Action<T, ProcessRecordsContext> handler) where T : class {
        EnsureRegistry().Register(handler);
        return this;
    }

    /// <summary>
    /// Builds the dispatching record processor.
    /// </summary>
    /// <returns>A new <see cref="DispatchingRecordProcessor"/> instance.</returns>
    public DispatchingRecordProcessor Build() {
        var registry = _registry ?? CreateRegistry();
        return new DispatchingRecordProcessor(registry);
    }

    IRecordProcessorRegistry EnsureRegistry() {
        if (_registry is not null) return _registry;
        _registry = CreateRegistry();
        return _registry;
    }

    ServiceProviderRegistry CreateRegistry() => new(
        invoker: _invoker,
        logger: _loggerFactory?.CreateLogger<ServiceProviderRegistry>(),
        onUnhandled: _onUnhandled
    );

    void EnsureNoExternalRegistry() {
        if (_registry is not null && _registry is not ServiceProviderRegistry)
            throw new InvalidOperationException(
                "Cannot configure invoker, logger, or unhandled callback when using an external registry. " +
                "Configure these options on the registry directly."
            );
    }
}
