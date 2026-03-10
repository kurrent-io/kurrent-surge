namespace Kurrent.Processors;

/// <summary>
/// Factory interface for creating <see cref="IRecordProcessor"/> instances.
/// Use this when you need to create new processor instances for each read operation.
/// </summary>
/// <example>
/// <code>
/// class MyProcessorFactory(ILogger logger) : IRecordProcessorFactory {
///     public IRecordProcessor CreateProcessor() => new MyProcessor(logger);
/// }
///
/// // Usage
/// await KcsProcess.Run(options, new MyProcessorFactory(logger));
/// </code>
/// </example>
[PublicAPI]
public interface IRecordProcessorFactory {
    /// <summary>
    /// Creates a new <see cref="IRecordProcessor"/> instance for processing records.
    /// This method is called once at the start of each read operation.
    /// </summary>
    /// <returns>A new processor instance to handle the read operation.</returns>
    IRecordProcessor CreateProcessor();
}

class PassthroughRecordProcessorFactory(IRecordProcessor processor) : IRecordProcessorFactory {
    public IRecordProcessor CreateProcessor() => processor;
}
