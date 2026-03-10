namespace Kurrent.Client.Streams;

/// <summary>
/// Static entry point for building global log read options.
/// </summary>
[PublicAPI]
public static class ReadLog {
    /// <summary>
    /// Start reading from the earliest position in the log (forwards).
    /// </summary>
    public static ReadLogBuilder FromStart() =>
        new ReadLogBuilder()
            .From(LogPosition.Earliest)
            .Forward();

    /// <summary>
    /// Start reading from the end of the log (backwards).
    /// </summary>
    public static ReadLogBuilder FromEnd() =>
        new ReadLogBuilder()
            .From(LogPosition.Latest)
            .Backwards();

    /// <summary>
    /// Start reading from a specific log position.
    /// </summary>
    public static ReadLogBuilder From(LogPosition position) => new ReadLogBuilder().From(position);
}

/// <summary>
/// Fluent builder for constructing <see cref="ReadOptions"/> instances.
/// </summary>
[PublicAPI]
public class ReadLogBuilder : OptionsBuilder<ReadLogBuilder, ReadOptions> {
    public ReadLogBuilder() : base(new ReadOptions()) { }

    /// <inheritdoc />
    protected override ReadOptions OnBuild(ReadOptions options) {
        options.ValidateOptions().Unwrap();
        return options;
    }

    /// <summary>
    /// Sets the starting log position.
    /// </summary>
    public ReadLogBuilder From(LogPosition position) =>
        With(options => options with { Start = position });

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public ReadLogBuilder Forward() =>
        With(options => options with { Direction = ReadDirection.Forwards });

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public ReadLogBuilder Backwards() =>
        With(options => options with { Direction = ReadDirection.Backwards });

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public ReadLogBuilder WithMaxRecords(long maxRecords) =>
        With(options => options with { MaxRecords = maxRecords });

    /// <summary>
    /// Set the buffer capacity for streaming reads.
    /// </summary>
    public ReadLogBuilder WithBufferCapacity(int bufferCapacity) =>
        With(options => options with { BufferCapacity = bufferCapacity });

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public ReadLogBuilder WithFilter(IRecordFilter filter) =>
        With(options => options with { Filter = filter });

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public ReadLogBuilder WithFilterExpression(string expression) =>
        With(options => options with { Filter = RecordFilter.ByExpression(expression) });

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public ReadLogBuilder SkipDecoding() =>
        With(options => options with { SkipDecoding = true });

    /// <summary>
    /// Set the cancellation token.
    /// </summary>
    public ReadLogBuilder WithCancellationToken(CancellationToken cancellationToken) =>
        With(options => options with { CancellationToken = cancellationToken });
}
