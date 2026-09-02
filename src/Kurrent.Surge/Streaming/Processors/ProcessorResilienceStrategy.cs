using System.Collections.Frozen;
using Google.Rpc;
using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Processors.Configuration;
using Polly;
using Polly.Retry;

namespace Kurrent.Surge.Processors.Configuration;

/// <summary>
/// The SDK's default subscription reconnection strategy and the resilience-context keys
/// shared between the SDK and user-supplied strategies.
/// </summary>
[PublicAPI]
public static class ProcessorResilienceStrategy {

    /// <summary>
    /// Kurrent error codes the server documents as retriable. They cover both exception shapes
    /// the subscribe path surfaces: typed <see cref="KurrentException"/>s translated on the
    /// connect phase, and raw <see cref="RpcException"/>s carrying the error code as an
    /// <see cref="ErrorInfo"/> detail while streaming.
    /// </summary>
    static readonly FrozenSet<string> RetryableErrorCodes = new[] {
        (string)NotLeaderNodeException.Code,
        (string)LeaderForwardingTimeoutException.Code,
        (string)ServerNotReadyException.Code,
        (string)ServerOverloadedException.Code
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The SDK's default retry strategy for subscription reconnection. Append it when composing
    /// a replacement pipeline via
    /// <see cref="ProcessorOptions.ConfigureResilience(Action{ResiliencePipelineBuilder})"/>
    /// to keep the SDK's transient-error knowledge.
    /// </summary>
    /// <remarks>
    /// The subscribe path surfaces transient failures in two shapes, and the predicate handles
    /// both: the connect phase translates server errors into typed <see cref="KurrentException"/>s
    /// (matched by retriable error code — not-leader, leader-forwarding-timeout, server-not-ready,
    /// server-overloaded), while the streaming phase propagates raw <see cref="RpcException"/>s
    /// (matched by status code, or by the same error codes when the server attaches structured
    /// error details). <see cref="IOException"/> is always considered transient.
    /// </remarks>
    public static RetryStrategyOptions DefaultRetry(ProcessorResilienceOptions? defaults = null) {
        var knobs = defaults ?? new ProcessorResilienceOptions();

        return new RetryStrategyOptions {
            ShouldHandle     = args => ValueTask.FromResult(args.Outcome.Exception is { } ex && (knobs.ShouldRetry ?? DefaultShouldRetry)(ex)),
            MaxRetryAttempts = knobs.MaxRetryAttempts,
            Delay            = knobs.BaseDelay,
            MaxDelay         = knobs.MaxDelay,
            BackoffType      = DelayBackoffType.Exponential,
            UseJitter        = true,
            OnRetry = args => {
                args.Context.Logger.LogWarning(
                    args.Outcome.Exception,
                    "Subscription reconnecting in {Delay} (attempt {Attempt})",
                    args.RetryDelay, args.AttemptNumber + 1);

                return default;
            }
        };
    }

    /// <summary>
    /// gRPC status codes treated as transient when a raw <see cref="RpcException"/> surfaces
    /// without a Kurrent error code.
    /// </summary>
    static readonly StatusCode[] DefaultRetryableStatusCodes = [
        StatusCode.Unavailable,
        StatusCode.Unknown,
        StatusCode.ResourceExhausted,
        StatusCode.Internal,
        StatusCode.Aborted
    ];

    /// <summary>
    /// The SDK's default transient-error classification, applied when
    /// <see cref="ProcessorResilienceOptions.ShouldRetry"/> is not set. Compose with it to
    /// extend rather than replace the SDK's knowledge:
    /// <c>r.ShouldRetry = ex =&gt; ex is TimeoutException || ProcessorResilienceStrategy.DefaultShouldRetry(ex);</c>
    /// </summary>
    public static bool DefaultShouldRetry(Exception exception) =>
        exception switch {
            KurrentException kex => RetryableErrorCodes.Contains(kex.ErrorDetails.ErrorCode.Value),
            RpcException rex     => DefaultRetryableStatusCodes.Contains(rex.StatusCode) || HasRetryableErrorCode(rex),
            IOException          => true,
            _                    => false
        };

    static bool HasRetryableErrorCode(RpcException exception) =>
        exception.GetRpcStatus()?.GetDetail<ErrorInfo>()?.Reason is { Length: > 0 } reason &&
        RetryableErrorCodes.Contains(reason);

    extension(ProcessorOptions options) {
        /// <summary>
        /// Builds the subscription reconnection pipeline from the configured mode: replacement wins,
        /// otherwise user extensions are prepended and the SDK default retry appended. Polly telemetry
        /// is wired here so every strategy reports through the processor's logging.
        /// </summary>
        /// <remarks>
        /// Lives with the strategy rather than on <see cref="ProcessorOptions"/> so the engine stays
        /// transport-agnostic: the options only carry the configured pipeline pieces, and the
        /// gRPC-aware default retry is attached here in the KurrentDB implementation.
        /// </remarks>
        internal ResiliencePipeline BuildResiliencePipeline(ILoggerFactory loggerFactory) {
            var builder = new ResiliencePipelineBuilder().ConfigureTelemetry(loggerFactory);

            if (options.ResilienceReplacement is not null) {
                options.ResilienceReplacement(builder);
                return builder.Build();
            }

            options.ResilienceExtension?.Invoke(builder);

            // MaxRetryAttempts = 0 means "retries disabled"; Polly rejects it at build time, so the
            // default strategy is simply not added.
            if (options.Resilience.MaxRetryAttempts > 0)
                builder.AddRetry(DefaultRetry(options.Resilience));

            return builder.Build();
        }
    }
}
