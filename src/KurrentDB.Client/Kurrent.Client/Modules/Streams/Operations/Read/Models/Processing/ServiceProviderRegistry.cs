using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Client.Streams;

/// <summary>
/// Registry implementation using <see cref="ServiceCollection"/>/<see cref="IServiceProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are registered in an internal <see cref="ServiceCollection"/> and resolved
/// via <see cref="IServiceProvider"/> on first invocation. The provider is built lazily
/// to allow registration before use.
/// </para>
/// <para>
/// The invoker strategy can be customized via constructor. Defaults to
/// <see cref="CompiledExpressionInvoker"/> for optimal performance.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var registry = new ServiceProviderRegistry(
///     invoker: new DynamicInvoker(),
///     logger: loggerFactory.CreateLogger&lt;ServiceProviderRegistry&gt;(),
///     onUnhandled: r =&gt; Console.WriteLine($"Unhandled: {r.ValueType}")
/// );
///
/// registry.Register&lt;OrderPlaced, OrderPlacedProcessor&gt;();
/// registry.Register&lt;OrderShipped&gt;((value, ctx) =&gt; { ... });
/// </code>
/// </example>
[PublicAPI]
public sealed class ServiceProviderRegistry : IRecordProcessorRegistry {
    readonly IServiceCollection      _services;
    readonly IRecordProcessorInvoker _invoker;
    readonly ILogger                 _logger;
    readonly Action<Record>?         _onUnhandled;

    IServiceProvider? _provider;
    bool              _isBuilt;

    /// <summary>
    /// Creates a new registry with optional configuration.
    /// </summary>
    /// <param name="invoker">
    /// The invoker to use for handler invocation. Defaults to <see cref="CompiledExpressionInvoker"/>.
    /// </param>
    /// <param name="logger">
    /// Logger for debug messages. Defaults to <see cref="NullLogger"/>.
    /// </param>
    /// <param name="onUnhandled">
    /// Callback invoked when a record has no registered handler.
    /// </param>
    public ServiceProviderRegistry(
        IRecordProcessorInvoker? invoker     = null,
        ILogger?                 logger      = null,
        Action<Record>?          onUnhandled = null
    ) {
        _services    = new ServiceCollection();
        _invoker     = invoker ?? new CompiledExpressionInvoker();
        _logger      = logger ?? NullLogger.Instance;
        _onUnhandled = onUnhandled;
    }

    /// <inheritdoc />
    public void Register<T, THandler>()
        where T : class
        where THandler : class, IRecordProcessor<T> {
        EnsureNotBuilt();
        _services.AddSingleton<IRecordProcessor<T>, THandler>();
        _services.AddSingleton<IRecordProcessor>(sp => sp.GetRequiredService<IRecordProcessor<T>>());
    }

    /// <inheritdoc />
    public void Register<T>(IRecordProcessor<T> handler) where T : class {
        EnsureNotBuilt();
        _services.AddSingleton(handler);
        _services.AddSingleton<IRecordProcessor>(handler);
    }

    /// <inheritdoc />
    public void Register<T>(Func<T, ProcessRecordsContext, ValueTask> handler) where T : class {
        EnsureNotBuilt();
        var processor = new DelegateRecordProcessor<T>(handler);
        _services.AddSingleton<IRecordProcessor<T>>(processor);
        _services.AddSingleton<IRecordProcessor>(processor);
    }

    /// <inheritdoc />
    public void Register<T>(Action<T, ProcessRecordsContext> handler) where T : class {
        Register<T>((value, ctx) => {
            handler(value, ctx);
            return ValueTask.CompletedTask;
        });
    }

    /// <inheritdoc />
    public async ValueTask InvokeAsync(int recordIndex, ProcessRecordsContext context) {
        EnsureBuilt();

        var record = context.Records[recordIndex];

        // 1. Decode if needed
        if (!record.IsDecoded)
            await record.TryDecode(context.CancellationToken).ConfigureAwait(false);

        // 2. Null check after decode
        if (record.Value is null) {
            _logger.LogDebug(
                "Skipping record {RecordId} at {Position}: Value is null",
                record.Id,
                record.Position
            );
            return;
        }

        // 3. Resolve handler
        var valueType   = record.ValueType;
        var handlerType = typeof(IRecordProcessor<>).MakeGenericType(valueType);
        var handler     = _provider!.GetService(handlerType);

        if (handler is null) {
            _logger.LogDebug(
                "No handler registered for record {RecordId} at {Position} with ValueType {ValueType}",
                record.Id,
                record.Position,
                valueType.FullName
            );
            _onUnhandled?.Invoke(record);
            return;
        }

        // 4. Invoke
        await _invoker.InvokeAsync(handler, record.Value, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IEnumerable<IRecordProcessor> GetAllHandlers() {
        EnsureBuilt();
        return _provider!.GetServices<IRecordProcessor>();
    }

    void EnsureBuilt() {
        if (_isBuilt) return;

        lock (_services) {
            if (_isBuilt) return;
            _provider = _services.BuildServiceProvider();
            _isBuilt  = true;
        }
    }

    void EnsureNotBuilt() {
        if (_isBuilt)
            throw new InvalidOperationException(
                "Cannot register handlers after the registry has been used. " +
                "Register all handlers before invoking or getting handlers."
            );
    }
}
