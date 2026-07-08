namespace Kurrent.Processors.Configuration;

/// <summary>
/// Knobs for the SDK's default reconnection pipeline — retry attempts, backoff, and the
/// transient-error decision. Tweak via
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
    /// Decides whether a failed subscribe attempt is transient and should trigger a
    /// reconnection. Receives the raw exception; return <see langword="true"/> to retry.
    /// When null, the implementation's default classification applies — retriable Kurrent
    /// error codes, transient gRPC status codes, and IO failures. Errors classified as
    /// non-transient propagate to the processor immediately.
    /// </summary>
    public Predicate<Exception>? ShouldRetry { get; set; }
}
