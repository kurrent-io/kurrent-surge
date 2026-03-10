namespace Kurrent.Client.Streams;

/// <summary>
/// Static entry point for building named stream read options.
/// </summary>
[PublicAPI]
public static class ReadStream {
    /// <summary>
    /// Start building read options for the specified stream.
    /// </summary>
    public static ReadStreamBuilder From(StreamName stream) => new ReadStreamBuilder().ForStream(stream);

    /// <summary>
    /// Start building read options for the specified stream name.
    /// </summary>
    public static ReadStreamBuilder From(string stream) => new ReadStreamBuilder().ForStream(stream);
}

/// <summary>
/// Fluent builder for constructing <see cref="ReadStreamOptions"/> instances.
/// </summary>
[PublicAPI]
public class ReadStreamBuilder : OptionsBuilder<ReadStreamBuilder, ReadStreamOptions> {
    public ReadStreamBuilder() : base(new ReadStreamOptions()) { }

    /// <inheritdoc />
    protected override ReadStreamOptions OnBuild(ReadStreamOptions options) {
        options.ValidateOptions().Unwrap();
        return options;
    }

    /// <summary>
    /// Set the stream to read from.
    /// </summary>
    public ReadStreamBuilder ForStream(StreamName stream) =>
        With(options => options with { Stream = stream });

    /// <summary>
    /// Set the stream to read from.
    /// </summary>
    public ReadStreamBuilder ForStream(string stream) =>
        With(options => options with { Stream = StreamName.From(stream) });

    /// <summary>
    /// Start from the beginning of the stream (forwards).
    /// </summary>
    public ReadStreamBuilder FromStart() =>
        With(options => options with { Start = Revision.Min, Direction = ReadDirection.Forwards });

    /// <summary>
    /// Start from the end of the stream (backwards).
    /// </summary>
    public ReadStreamBuilder FromEnd() =>
        With(options => options with { Start = Revision.Max, Direction = ReadDirection.Backwards });

    /// <summary>
    /// Start reading from a specific stream revision.
    /// </summary>
    public ReadStreamBuilder FromRevision(Revision revision) =>
        With(options => options with { Start = revision });

    /// <summary>
    /// Read forwards (toward newer records).
    /// </summary>
    public ReadStreamBuilder Forward() =>
        With(options => options with { Direction = ReadDirection.Forwards });

    /// <summary>
    /// Read backwards (toward older records).
    /// </summary>
    public ReadStreamBuilder Backwards() =>
        With(options => options with { Direction = ReadDirection.Backwards });

    /// <summary>
    /// Limit the number of records returned.
    /// </summary>
    public ReadStreamBuilder WithMaxRecords(long maxRecords) =>
        With(options => options with { MaxRecords = maxRecords });

    /// <summary>
    /// Set the buffer capacity for streaming reads.
    /// </summary>
    public ReadStreamBuilder WithBufferCapacity(int bufferCapacity) =>
        With(options => options with { BufferCapacity = bufferCapacity });

    /// <summary>
    /// Apply a record filter.
    /// </summary>
    public ReadStreamBuilder WithFilter(IRecordFilter filter) =>
        With(options => options with { Filter = filter });

    /// <summary>
    /// Apply a filter expression. Sugar for <c>RecordFilter.ByExpression(...)</c>.
    /// </summary>
    public ReadStreamBuilder WithFilterExpression(string expression) =>
        With(options => options with { Filter = RecordFilter.ByExpression(expression) });

    /// <summary>
    /// Skip record data deserialization.
    /// </summary>
    public ReadStreamBuilder SkipDecoding() =>
        With(options => options with { SkipDecoding = true });

    /// <summary>
    /// Set the cancellation token.
    /// </summary>
    public ReadStreamBuilder WithCancellationToken(CancellationToken cancellationToken) =>
        With(options => options with { CancellationToken = cancellationToken });
}
