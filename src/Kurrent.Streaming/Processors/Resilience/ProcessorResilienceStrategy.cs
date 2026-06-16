using System.Collections.Frozen;
using Google.Rpc;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Processors.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Kurrent.Streaming.Options;

/// <summary>
/// The SDK's default subscription reconnection strategy and the resilience-context keys
/// shared between the SDK and user-supplied strategies.
/// </summary>
[PublicAPI]
public static class ProcessorResilienceStrategy {
    /// <summary>
    /// Resilience-context key carrying the processor's <see cref="ILogger"/>. The SDK sets it
    /// before executing the pipeline, so both the default retry and any user strategy added via
    /// <see cref="ProcessorOptions.ExtendResilience"/> or
    /// <see cref="ProcessorOptions.ConfigureResilience(Action{ResiliencePipelineBuilder})"/>
    /// can log from their callbacks:
    /// <c>args.Context.Properties.TryGetValue(ProcessorResilienceStrategy.LoggerKey, out var logger)</c>.
    /// </summary>
    public static readonly ResiliencePropertyKey<ILogger> LoggerKey = new("kurrent.processor.logger");

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
            ShouldHandle     = args => ValueTask.FromResult(IsRetryable(args.Outcome.Exception, knobs.RetryableStatusCodes)),
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

    static bool IsRetryable(Exception? exception, StatusCode[] retryableStatusCodes) =>
        exception switch {
            KurrentException kex => RetryableErrorCodes.Contains(kex.ErrorDetails.ErrorCode.Value),
            RpcException rex     => retryableStatusCodes.Contains(rex.StatusCode) || HasRetryableErrorCode(rex),
            IOException          => true,
            _                    => false
        };

    static bool HasRetryableErrorCode(RpcException exception) =>
        exception.GetRpcStatus()?.GetDetail<ErrorInfo>()?.Reason is { Length: > 0 } reason &&
        RetryableErrorCodes.Contains(reason);
}
