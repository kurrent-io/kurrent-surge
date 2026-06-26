using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using static System.StringComparison;

namespace Kurrent.Client.Streams;

/// <summary>
/// Base type for filters that target KurrentDB secondary index streams (<c>$idx-*</c>).
/// Routed through <c>$all</c> with a <c>Filter.Prefix</c> matching the <see cref="IndexName"/>.
/// </summary>
public abstract record IndexFilter : RecordFilter {
    public override string Expression { get; init; } = null!;

    /// <summary>
    /// The internal name of the index that this filter matches. Eg: "$idx-ce-race"
    /// </summary>
    internal string IndexName { get; init; } = null!;
}

/// <summary>
/// A filter targeting the <c>$idx-ce-{category}</c> secondary index stream.
/// Accepts plain category names, <c>$ce-</c>, or <c>$idx-ce-</c> prefixed values.
/// </summary>
public sealed partial record CategoryFilter : IndexFilter {
    // Accepts optional $idx-ce- or $ce- prefix, captures the clean category name.
    // Name must be non-empty with no whitespace or $ characters.
    [GeneratedRegex(@"^(?:\$idx-ce-|\$ce-)?([^\s$]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    static partial Regex CategoryPattern { get; }

    CategoryFilter() { }

    /// <summary>
    /// The clean category name used for client-side matching.
    /// Always the plain name (e.g., "orders"), regardless of how the filter was constructed.
    /// </summary>
    public string CategoryName { get; private init; } = null!;

    public override bool IsMatch(Record record) =>
        record.Category.Equals(CategoryName, OrdinalIgnoreCase);

    public static CategoryFilter Parse(string value) =>
        TryParse(value, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(value));

    public static bool TryParse(string value, [NotNullWhen(true)] out CategoryFilter? filter, out string? error) {
        filter = null;
        error  = null;

        value = value.Trim();

        if (value.Length == 0) {
            error = "Category filter value must not be empty.";
            return false;
        }

        var match = CategoryPattern.Match(value);
        if (!match.Success) {
            error = $"Invalid category filter value: '{value}'. Must not contain whitespace or '$' (except known prefixes $idx-ce-, $ce-).";
            return false;
        }

        var categoryName = match.Groups[1].Value;

        filter = new() {
            Expression   = value,
            CategoryName = categoryName,
            IndexName    = $"$idx-ce-{categoryName}"
        };
        return true;
    }
}

/// <summary>
/// A filter targeting the <c>$idx-et-{schema}</c> secondary index stream.
/// Accepts plain schema (event type) names, <c>$et-</c>, or <c>$idx-et-</c> prefixed values.
/// </summary>
public sealed partial record SchemaFilter : IndexFilter {
    // Accepts optional $idx-et- or $et- prefix, captures the clean schema name.
    // Name must be non-empty with no whitespace or $ characters.
    [GeneratedRegex(@"^(?:\$idx-et-|\$et-)?([^\s$]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    static partial Regex SchemaPattern { get; }

    SchemaFilter() { }

    /// <summary>
    /// The clean schema (event type) name used for client-side matching.
    /// Always the plain name (e.g., "OrderPlaced"), regardless of how the filter was constructed.
    /// </summary>
    public string SchemaName { get; private init; } = null!;

    public override bool IsMatch(Record record) =>
        record.Schema.Schema.Value.Equals(SchemaName, OrdinalIgnoreCase);

    public static SchemaFilter Parse(string value) =>
        TryParse(value, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(value));

    public static bool TryParse(string value, [NotNullWhen(true)] out SchemaFilter? filter, out string? error) {
        filter = null;
        error  = null;

        value = value.Trim();

        if (value.Length == 0) {
            error = "Schema filter value must not be empty.";
            return false;
        }

        var match = SchemaPattern.Match(value);
        if (!match.Success) {
            error = $"Invalid schema filter value: '{value}'. Must not contain whitespace or '$' (except known prefixes $idx-et-, $et-).";
            return false;
        }

        var schemaName = match.Groups[1].Value;

        filter = new() {
            Expression = value,
            SchemaName = schemaName,
            IndexName  = $"$idx-et-{schemaName}"
        };
        return true;
    }
}

/// <summary>
/// A filter targeting a <c>$idx-user-{name}</c> user-defined secondary index stream.
/// Name must be lowercase alphanumeric with underscores and hyphens, max 1000 characters.
/// </summary>
public sealed partial record UserIndexFilter : IndexFilter {
    // Accepts optional $idx-user- prefix.
    // Name must match server validation: lowercase alphanumeric, underscores, hyphens only, max 1000 chars.
    [GeneratedRegex(@"^(?:\$idx-user-)?([a-z0-9_-]{1,1000})$", RegexOptions.CultureInvariant)]
    static partial Regex UserIndexPattern { get; }
   
    UserIndexFilter() { }
    
    // Server handles all filtering for user-defined indexes; client-side matching is a no-op.
    public override bool IsMatch(Record record) => true;

    public static UserIndexFilter Parse(string value) =>
        TryParse(value, out var filter, out var error)
            ? filter
            : throw new ArgumentException(error, nameof(value));

    public static bool TryParse(string value, [NotNullWhen(true)] out UserIndexFilter? filter, out string? error) {
        filter = null;
        error  = null;

        value = value.Trim();

        if (value.Length == 0) {
            error = "User index filter value must not be empty.";
            return false;
        }

        var match = UserIndexPattern.Match(value);
        if (!match.Success) {
            error = $"Invalid user index filter value: '{value}'. Must contain only lowercase alphanumeric characters, underscores, and hyphens (max 1000 chars).";
            return false;
        }

        var indexValue = match.Groups[1].Value;

        filter = new() {
            Expression = value,
            IndexName  = $"$idx-user-{indexValue}"
        };
        return true;
    }
}
