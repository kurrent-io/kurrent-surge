namespace Kurrent.Client.Streams;

/// <summary>
/// A filter that matches records by schema name.
/// </summary>
[PublicAPI]
public sealed record SchemaFilter : RegexFilter {
    /// <summary>
    /// Creates a filter that matches a specific schema name exactly.
    /// </summary>
    public SchemaFilter(string schema) : base(schema) { }

    /// <summary>
    /// Creates a filter that matches records by schema name prefixes.
    /// </summary>
    public SchemaFilter(params string[] prefixes) : base(prefixes) { }

    protected override ReadOnlySpan<char> GetMatchValue(Record record) =>
        record.Schema.SchemaName.Value;
}
