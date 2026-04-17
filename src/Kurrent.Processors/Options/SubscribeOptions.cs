namespace Kurrent.Processors.Options;

/// <summary>
/// Processor registration. Supports inline typed handlers via <c>Process&lt;T&gt;()</c>
/// or pre-built processors via <c>Use()</c>.
/// </summary>
[PublicAPI]
public sealed class SubscribeOptions {
    MessageProcessor?        _inlineProcessor;
    IRecordProcessor?        _processor;
    IRecordProcessorFactory? _factory;

    /// <summary>
    /// Registers an async handler for messages of type <typeparamref name="T"/>.
    /// Returns the underlying <see cref="MessageProcessor"/> for chaining additional handlers.
    /// </summary>
    public MessageProcessor Process<T>(Func<T, ProcessRecordContext, ValueTask> handler) {
        _inlineProcessor ??= new MessageProcessor();
        _inlineProcessor.Process(handler);
        return _inlineProcessor;
    }

    /// <summary>
    /// Registers a synchronous handler for messages of type <typeparamref name="T"/>.
    /// Returns the underlying <see cref="MessageProcessor"/> for chaining additional handlers.
    /// </summary>
    public MessageProcessor Process<T>(Action<T, ProcessRecordContext> handler) {
        _inlineProcessor ??= new MessageProcessor();
        _inlineProcessor.Process(handler);
        return _inlineProcessor;
    }

    /// <summary>
    /// Uses a pre-built processor instance.
    /// </summary>
    public void Use(IRecordProcessor processor) => _processor = processor;

    /// <summary>
    /// Uses a processor factory that creates instances on demand.
    /// </summary>
    public void Use(IRecordProcessorFactory factory) => _factory = factory;

    internal IRecordProcessor ResolveProcessor() {
        if (_processor is not null)
            return _processor;

        if (_factory is not null) {
            try {
                return _factory.CreateProcessor();
            }
            catch (Exception ex) {
                throw new DroneException("WORKER_CREATION_FAILED", "Failed to create record processor instance.", ex);
            }
        }

        if (_inlineProcessor is not null)
            return _inlineProcessor;

        throw new InvalidOperationException(
            "No processor configured. Call Subscribe.Process<T>() to register handlers " +
            "or Subscribe.Use() to provide a processor instance.");
    }

    internal bool HasProcessor => _processor is not null || _factory is not null || _inlineProcessor is not null;
}
