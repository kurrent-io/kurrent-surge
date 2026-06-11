using Grpc.Core;

namespace Kurrent.Streaming.Options;

/// <summary>
/// Knobs for the SDK's default reconnection pipeline — retry attempts, backoff, and which
/// raw gRPC status codes count as transient. Tweak via
/// <see cref="ProcessorOptions.ConfigureResilience(Action{ProcessorResilienceOptions})"/>
/// or bind from configuration; no Polly types required.
/// </summary>
/// <remarks>
/// Reconnection resumes from the last observed position, so the processor sees a seamless
/// stream with no gaps. Only transient errors trigger retries; terminal errors
/// (stream deleted, position not found, cancellation) propagate immediately.
/// </remarks>
[PublicAPI]
public record ProcessorResilienceOptions {
    /// <summary>
    /// Maximum number of reconnection attempts before giving up. Defaults to
    /// <see cref="int.MaxValue"/> — retry indefinitely, keeping a long-running subscription
    /// alive across server restarts and network blips. Set to <c>0</c> to disable retries.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = int.MaxValue;

    /// <summary>
    /// Initial backoff before the first reconnection attempt. Subsequent delays grow
    /// exponentially with jitter, capped at <see cref="MaxDelay"/>.
    /// </summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Upper bound for the backoff delay between reconnection attempts.
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// gRPC status codes that trigger a reconnection when a raw <see cref="RpcException"/>
    /// surfaces without a Kurrent error code. Errors with any other status code propagate
    /// to the processor immediately instead of being retried.
    /// </summary>
    public StatusCode[] RetryableStatusCodes { get; set; } = [
        StatusCode.Unavailable,
        StatusCode.Unknown,
        StatusCode.ResourceExhausted,
        StatusCode.Internal,
        StatusCode.Aborted
    ];
}
