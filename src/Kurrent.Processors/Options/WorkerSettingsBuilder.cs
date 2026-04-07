namespace Kurrent.Processors.Options;

/// <summary>
/// Fluent builder for <see cref="WorkerSettings"/>.
/// </summary>
[PublicAPI]
public sealed class WorkerSettingsBuilder : OptionsBuilder<WorkerSettingsBuilder, WorkerSettings> {
    internal WorkerSettingsBuilder(WorkerSettings settings) : base(settings) { }

    /// <summary>
    /// Initializes a new instance with default settings.
    /// </summary>
    public WorkerSettingsBuilder() : base(new WorkerSettings()) { }

    // /// <summary>
    // /// Throws an exception on processing failure instead of logging and stopping.
    // /// </summary>
    // /// <param name="enabled">True to throw on failure (default: true when called).</param>
    // /// <returns>The builder instance for method chaining.</returns>
    // public WorkerSettingsBuilder ThrowOnFailure(bool enabled = true) =>
    //     With(s => s with { ThrowOnFailure = enabled });

    /// <summary>
    /// Sets the number of records to buffer in memory.
    /// </summary>
    /// <param name="capacity">Buffer capacity (default: 1000, max: 10000).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public WorkerSettingsBuilder BufferCapacity(int capacity) =>
        With(s => s with { BufferCapacity = capacity });

    /// <summary>
    /// Skips record data deserialization for performance-critical scenarios.
    /// </summary>
    /// <param name="skip">True to skip decoding (default: true when called).</param>
    /// <returns>The builder instance for method chaining.</returns>
    public WorkerSettingsBuilder SkipDecoding(bool skip = true) =>
        With(s => s with { SkipDecoding = skip });
}
