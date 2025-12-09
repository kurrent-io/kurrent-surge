namespace Kurrent.Client.Streams;

/// <summary>
/// A composite <see cref="IRecordProcessor"/> that routes records to type-specific handlers
/// via an <see cref="IRecordProcessorRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Records are processed <b>in stream order</b>, preserving the original sequence.
/// Each record is routed to its corresponding <see cref="IRecordProcessor{T}"/> based on
/// <see cref="Record.ValueType"/>.
/// </para>
/// <para>
/// Lifecycle methods (<see cref="Initialize"/>, <see cref="OnHeartbeat"/>, <see cref="OnShutdown"/>)
/// are forwarded to all registered handlers.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Using builder (recommended)
/// var processor = DispatchingRecordProcessor.Create()
///     .WithHandler&lt;OrderPlaced, OrderPlacedProcessor&gt;()
///     .WithHandler&lt;OrderShipped&gt;((value, ctx) =&gt; {
///         Console.WriteLine($"Shipped: {value.OrderId}");
///         return ValueTask.CompletedTask;
///     })
///     .Build();
///
/// // Using registry directly
/// var registry = new ServiceProviderRegistry();
/// registry.Register&lt;OrderPlaced, OrderPlacedProcessor&gt;();
/// var processor = new DispatchingRecordProcessor(registry);
/// </code>
/// </example>
[PublicAPI]
public sealed class DispatchingRecordProcessor : IRecordProcessor {
    readonly IRecordProcessorRegistry _registry;

    IRecordProcessor[]? _handlers;

    /// <summary>
    /// Creates a new dispatching processor with the specified registry.
    /// </summary>
    /// <param name="registry">The registry to use for handler resolution and invocation.</param>
    public DispatchingRecordProcessor(IRecordProcessorRegistry registry) {
        _registry = registry;
    }

    /// <summary>
    /// Creates a builder with a new <see cref="ServiceProviderRegistry"/>.
    /// </summary>
    /// <returns>A new <see cref="DispatchingRecordProcessorBuilder"/> instance.</returns>
    public static DispatchingRecordProcessorBuilder Create() => new();

    /// <summary>
    /// Creates a builder with the specified registry.
    /// </summary>
    /// <param name="registry">The registry to use.</param>
    /// <returns>A new <see cref="DispatchingRecordProcessorBuilder"/> instance.</returns>
    public static DispatchingRecordProcessorBuilder Create(IRecordProcessorRegistry registry) => new(registry);

    /// <inheritdoc />
    public async ValueTask Initialize(InitializationContext context) {
        _handlers = _registry.GetAllHandlers().ToArray();

        foreach (var handler in _handlers)
            await handler.Initialize(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        for (var i = 0; i < context.Records.Count; i++)
            await _registry.InvokeAsync(i, context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask OnHeartbeat(HeartbeatContext context) {
        if (_handlers is null) return;

        foreach (var handler in _handlers)
            await handler.OnHeartbeat(context).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask OnShutdown(ShutdownContext context) {
        if (_handlers is null) return;

        foreach (var handler in _handlers)
            await handler.OnShutdown(context).ConfigureAwait(false);
    }
}

/// <summary>
/// Internal processor that wraps a delegate for simple processing scenarios.
/// </summary>
internal sealed class DelegateRecordProcessor<T>(Func<T, ProcessRecordsContext, ValueTask> handler)
    : IRecordProcessor<T> where T : class {
    /// <inheritdoc />
    public ValueTask ProcessRecord(T value, ProcessRecordsContext context) => handler(value, context);
}
