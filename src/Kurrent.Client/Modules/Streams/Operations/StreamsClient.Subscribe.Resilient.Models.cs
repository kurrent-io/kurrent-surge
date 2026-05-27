using Grpc.Core;

namespace Kurrent.Client.Streams;

/// <summary>
/// Configures the resilience strategy for <see cref="StreamsClient.SubscribeWithResilience"/>.
/// Controls how and when the subscription reconnects after transient failures.
/// </summary>
[PublicAPI]
public sealed record SubscribeResilienceOptions {
    /// <summary>
    /// Default profile: retry forever with exponential backoff and jitter.
    /// Suitable for long-running subscriptions that must stay alive.
    /// </summary>
    public static readonly SubscribeResilienceOptions Default = new();

    /// <summary>
    /// Maximum number of retry attempts before giving up.
    /// Set to <see cref="int.MaxValue"/> (default) to retry indefinitely.
    /// </summary>
    public int MaxRetryAttempts { get; init; } = int.MaxValue;

    /// <summary>
    /// Initial delay before the first reconnection attempt.
    /// Subsequent delays grow exponentially with jitter up to <see cref="MaxDelay"/>.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Upper bound for the backoff delay between reconnection attempts.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Bounded channel capacity between the gRPC producer and the consumer.
    /// Provides backpressure: a slow consumer will pause the producer when the buffer is full.
    /// </summary>
    public int ChannelCapacity { get; init; } = 256;

    /// <summary>
    /// The gRPC status codes that should trigger a reconnection attempt.
    /// Errors with status codes not in this set propagate immediately to the consumer.
    /// </summary>
    public StatusCode[] RetryableStatusCodes { get; init; } = [
        StatusCode.Unavailable,
        StatusCode.Unknown,
        StatusCode.ResourceExhausted,
        StatusCode.Internal,
        StatusCode.Aborted
    ];
}
