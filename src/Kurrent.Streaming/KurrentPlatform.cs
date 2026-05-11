using Kurrent.Streaming.Options;

namespace Kurrent.Streaming;

/// <summary>
/// Unified entry point for the Kurrent platform.
/// Future features will hang off this class:
/// <c>KurrentPlatform.Client()</c>, <c>KurrentPlatform.Connector()</c>, etc.
/// </summary>
/// <example>
/// <code>
/// await KurrentPlatform.Processor(new OrderProcessor(), opts => {
///     opts.Connection("kurrentdb://localhost:2113");
/// }).RunAsync(ct);
/// </code>
/// </example>
[PublicAPI]
public static class KurrentPlatform {
    /// <summary>
    /// Creates and configures a processor from a prebuilt <see cref="IRecordProcessor"/>.
    /// </summary>
    public static KurrentProcessor CreateProcessor(IRecordProcessor processor, Action<ProcessorOptions>? configure = null) =>
        KurrentProcessor.Create(processor, configure);

    /// <summary>
    /// Creates and configures a processor using an inline <see cref="MessageProcessor"/>.
    /// </summary>
    public static KurrentProcessor Processor(Action<MessageProcessor> handlers, Action<ProcessorOptions>? configure = null) =>
        KurrentProcessor.Create(handlers, configure);
}
