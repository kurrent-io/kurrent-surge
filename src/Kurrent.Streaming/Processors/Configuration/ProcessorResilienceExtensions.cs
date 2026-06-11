using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace Kurrent.Streaming.Options;

/// <summary>
/// Convenience access to the processor logger from resilience callbacks, resolving
/// <see cref="ProcessorResilienceStrategy.LoggerKey"/> without touching context properties.
/// </summary>
[PublicAPI]
public static class ProcessorResilienceExtensions {
    extension(ResilienceContext context) {
        /// <summary>
        /// The processor logger carried in the resilience context, or a no-op logger when the
        /// pipeline executes outside a processor (manual <c>ExecuteAsync</c> without a primed context).
        /// </summary>
        public ILogger Logger =>
            context.Properties.GetValue(ProcessorResilienceStrategy.LoggerKey, NullLogger.Instance);
    }

    extension<TResult>(OnRetryArguments<TResult> args) {
        /// <summary>
        /// The processor logger, straight from the retry callback:
        /// <c>OnRetry = args =&gt; { args.Logger.LogWarning(...); return default; }</c>.
        /// </summary>
        public ILogger Logger => args.Context.Logger;
    }
}
