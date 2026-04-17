using Kurrent.Processors.Options;

namespace Kurrent.Processors;

/// <summary>
/// Unified entry point for the Kurrent platform.
/// Future features will hang off this class:
/// <c>KurrentPlatform.Client()</c>, <c>KurrentPlatform.Connector()</c>, etc.
/// </summary>
/// <example>
/// <code>
/// await KurrentPlatform.Processor(opts => {
///     opts.Connection("kurrentdb://localhost:2113");
///     opts.Subscribe.Use(new OrderProcessor());
/// }).RunAsync(ct);
/// </code>
/// </example>
[PublicAPI]
public static class KurrentPlatform {
    /// <summary>
    /// Creates and configures a processor.
    /// </summary>
    /// <param name="configure">Action to configure the processor options.</param>
    /// <returns>A runner with <c>Build()</c>, <c>RunAsync()</c>, and <c>RunWithGracefulShutdownAsync()</c> methods.</returns>
    public static KurrentProcessorRunner Processor(Action<ProcessorOptions> configure) =>
        KurrentProcessor.Create(configure);
}
