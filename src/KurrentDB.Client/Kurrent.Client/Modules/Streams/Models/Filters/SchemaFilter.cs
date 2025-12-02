using System.Text.RegularExpressions;

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
    /// Creates a filter that matches any of the specified schema names exactly.
    /// </summary>
    public SchemaFilter(string[] schemas, bool exactMatch) : base(schemas, exactMatch) { }

    /// <summary>
    /// Creates a filter from a raw regex pattern.
    /// </summary>
    /// <param name="pattern">The compiled regex pattern.</param>
    public SchemaFilter(Regex pattern) : base(pattern) { }

    protected override ReadOnlySpan<char> GetMatchValue(Record record) =>
        record.Schema.SchemaName.Value;
}
