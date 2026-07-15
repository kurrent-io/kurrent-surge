using Kurrent.Processors.Configuration;
using Polly;

namespace Kurrent.Surge.Streaming.Options;

/// <summary>
/// Convenience access to the processor logger from resilience callbacks without touching
/// context properties.
/// </summary>
[PublicAPI]
public static class ProcessorResilienceExtensions {
    /// <summary>
    /// Resilience-context key carrying the processor's <see cref="ILogger"/>. The SDK sets it
    /// before executing the pipeline, so both the default retry and any user strategy added via
    /// <see cref="ProcessorOptions.ExtendResilience"/> or
    /// <see cref="ProcessorOptions.ConfigureResilience(Action{ResiliencePipelineBuilder})"/>
    /// can log from their callbacks through <c>args.Context.Logger</c>.
    /// </summary>
    static readonly ResiliencePropertyKey<ILogger> LoggerKey = new("kurrent.processor.logger");

    extension(ResilienceContext context) {
        /// <summary>
        /// The processor logger carried in the resilience context, or a no-op logger when the
        /// pipeline executes outside a processor (manual <c>ExecuteAsync</c> without a primed
        /// context). Every Polly callback exposes the context, so any strategy can log:
        /// <c>OnRetry = args =&gt; { args.Context.Logger.LogWarning(...); return default; }</c>.
        /// </summary>
        public ILogger Logger {
            get => context.Properties.GetValue(LoggerKey, NullLogger.Instance);
            set => context.Properties.Set(LoggerKey, value);
        }
    }
}
