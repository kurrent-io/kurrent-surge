namespace Kurrent.Client.Streams;

/// <summary>
/// Fluent builder for <see cref="BatchingSettings"/>.
/// </summary>
[PublicAPI]
public sealed class BatchingSettingsBuilder : OptionsBuilder<BatchingSettingsBuilder, BatchingSettings> {
    internal BatchingSettingsBuilder(BatchingSettings settings) : base(settings) { }

    /// <summary>
    /// Initializes a new instance with default settings.
    /// </summary>
    public BatchingSettingsBuilder() : base(new BatchingSettings()) { }

    /// <summary>
    /// Sets the maximum number of records per batch.
    /// </summary>
    /// <param name="maxSize">Maximum batch size (default: 200).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public BatchingSettingsBuilder MaxSize(int maxSize) =>
        With(s => s with { MaxSize = maxSize });

    /// <summary>
    /// Sets the maximum time to wait for a full batch.
    /// </summary>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public BatchingSettingsBuilder Timeout(long timeout) =>
        With(s => s with { Timeout = timeout });

    /// <summary>
    /// Sets the maximum time to wait for a full batch.
    /// </summary>
    /// <param name="timeout">Timeout as a TimeSpan.</param>
    /// <returns>The builder instance for method chaining.</returns>
    public BatchingSettingsBuilder Timeout(TimeSpan timeout) =>
        With(s => s with { Timeout = (long)timeout.TotalMilliseconds });
}
