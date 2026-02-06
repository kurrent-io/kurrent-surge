namespace Kurrent.Processors.Options;

/// <summary>
/// Fluent builder for <see cref="CheckpointSettings"/>.
/// </summary>
[PublicAPI]
public sealed class CheckpointSettingsBuilder : OptionsBuilder<CheckpointSettingsBuilder, CheckpointSettings> {
    internal CheckpointSettingsBuilder(CheckpointSettings settings) : base(settings) { }

    /// <summary>
    /// Initializes a new instance with default settings.
    /// </summary>
    public CheckpointSettingsBuilder() : base(new CheckpointSettings()) { }

    /// <summary>
    /// Sets the time between automatic checkpoints.
    /// </summary>
    /// <param name="interval">Interval in milliseconds.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder Interval(long interval) =>
        With(s => s with { Interval = interval });

    /// <summary>
    /// Sets the time between automatic checkpoints.
    /// </summary>
    /// <param name="interval">Interval as a TimeSpan.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder Interval(TimeSpan interval) =>
        With(s => s with { Interval = (long)interval.TotalMilliseconds });

    /// <summary>
    /// Sets the maximum time to wait for workers to acknowledge checkpoint.
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder Timeout(long timeout) =>
        With(s => s with { Timeout = timeout });

    /// <summary>
    /// Sets the maximum time to wait for workers to acknowledge checkpoint.
    /// </summary>
    /// <param name="timeout">Timeout as a TimeSpan.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder Timeout(TimeSpan timeout) =>
        With(s => s with { Timeout = (long)timeout.TotalMilliseconds });

    /// <summary>
    /// Sets the number of completed checkpoints to retain.
    /// </summary>
    /// <param name="count">Retention count (default: 3).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder RetentionCount(int count) =>
        With(s => s with { RetentionCount = count });

    /// <summary>
    /// Sets what happens when a checkpoint fails.
    /// </summary>
    /// <param name="policy">The failure policy to use.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public CheckpointSettingsBuilder FailurePolicy(CheckpointFailurePolicy policy) =>
        With(s => s with { FailurePolicy = policy });
}
