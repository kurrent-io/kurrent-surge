namespace Kurrent.Client.Streams;

/// <summary>
/// Entry point for the Processors feature. Creates and runs processor workers.
/// </summary>
/// <remarks>
/// <para>
/// Provides a fluent API for configuring and executing record processors.
/// Use <see cref="Create(IRecordProcessor)"/> or <see cref="Create(IRecordProcessorFactory)"/>
/// to start building a processor configuration.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Simple fire-and-forget
/// await KurrentProcessors.Create(new MyProcessor())
///     .ConnectTo("kurrentdb://localhost:2113")
///     .WithProcessorId("order-processor")
///     .WithFilter(RecordFilter.ByStream("orders"))
///     .RunAsync(stoppingToken);
///
/// // Advanced: Get worker for runtime access
/// var worker = KurrentProcessors.Create(new MyProcessor())
///     .ConnectTo("kurrentdb://localhost:2113")
///     .WithProcessorId("order-processor")
///     .Build();
///
/// Console.WriteLine($"Status: {worker.Status}");
/// await worker.RunAsync(stoppingToken);
/// </code>
/// </example>
[PublicAPI]
public static class KurrentProcessors {
    /// <summary>
    /// Creates a builder for the specified processor factory.
    /// </summary>
    /// <param name="processorFactory">Factory to create processor instances.</param>
    /// <returns>A <see cref="KurrentProcessorBuilder"/> for fluent configuration.</returns>
    /// <remarks>
    /// <para>
    /// Use a factory when you need a new processor instance for each processing operation,
    /// such as when the processor maintains state or requires dependency injection.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// await KurrentProcessors.Create(new MyProcessorFactory(logger))
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithProcessorId("order-processor")
    ///     .WithFilter(RecordFilter.ByStream("orders"))
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public static KurrentProcessorBuilder Create(IRecordProcessorFactory processorFactory) => new(processorFactory);

    /// <summary>
    /// Creates a builder for the specified processor instance.
    /// </summary>
    /// <param name="processor">The record processor to run.</param>
    /// <returns>A <see cref="KurrentProcessorBuilder"/> for fluent configuration.</returns>
    /// <example>
    /// <code>
    /// await KurrentProcessors.Create(new MyProcessor())
    ///     .ConnectTo("kurrentdb://localhost:2113")
    ///     .WithProcessorId("order-processor")
    ///     .RunAsync(stoppingToken);
    /// </code>
    /// </example>
    public static KurrentProcessorBuilder Create(IRecordProcessor processor) => Create(new PassthroughRecordProcessorFactory(processor));
}
