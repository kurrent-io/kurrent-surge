using Kurrent.Processors.Options;

namespace Kurrent.Processors;

/// <summary>
/// Entry point for creating and running Kurrent processors.
/// </summary>
/// <example>
/// <code>
/// await KurrentProcessor.Create(opts => {
///     opts.Connection("kurrentdb://localhost:2113");
///     opts.ProcessorId = "order-processor";
///     opts.Subscription.Filter = RecordFilter.ByStream("orders");
///
///     opts.Checkpoint.Interval = Duration.FromSeconds(10);
///
///     opts.Subscribe
///         .Process&lt;OrderPlaced&gt;(async (msg, ctx) => { /* ... */ })
///         .Process&lt;OrderShipped&gt;((msg, ctx) => { /* sync */ });
/// }).RunAsync(ct);
/// </code>
/// </example>
[PublicAPI]
public static class KurrentProcessor {
    /// <summary>
    /// Creates a processor from a configuration lambda.
    /// </summary>
    public static KurrentProcessorRunner Create(Action<ProcessorOptions> configure) {
        var options = new ProcessorOptions();
        configure(options);
        return new KurrentProcessorRunner(options);
    }
}
