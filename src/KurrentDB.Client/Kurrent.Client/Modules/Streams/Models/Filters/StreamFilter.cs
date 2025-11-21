namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records by stream name.
/// </summary>
[PublicAPI]
public sealed record StreamFilter : RegexFilter {
    /// <summary>
    /// Creates a filter that matches a specific stream name.
    /// </summary>
    public StreamFilter(StreamName stream) : base(stream.Value) =>
        TargetStream = stream;

    /// <summary>
    /// Creates a filter that matches records by stream name prefixes.
    /// </summary>
    public StreamFilter(params string[] prefixes) : base(prefixes) { }

    protected override ReadOnlySpan<char> GetMatchValue(Record record) =>
        record.Stream.Value;
}
