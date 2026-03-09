using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

/// <summary>
/// Configuration for the query expression normalizer. Column and table identifiers found in
/// the query are matched against these dictionaries (case-insensitive) and replaced with
/// the mapped values.
/// </summary>
public sealed record NormalizerOptions {
    /// <summary>
    /// Case-insensitive map of column names to their replacements.
    /// </summary>
    public ImmutableDictionary<string, string> ColumnReplacements { get; init; } =
        ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Case-insensitive map of table names to their replacements.
    /// </summary>
    public ImmutableDictionary<string, string> TableReplacements { get; init; } =
        ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When set, any table identifier starting with this prefix (case-insensitive) causes
    /// normalization to fail with an error. Used to block access to internal/system tables.
    /// </summary>
    public string? ForbiddenTablePrefix { get; init; }
}

#endif
