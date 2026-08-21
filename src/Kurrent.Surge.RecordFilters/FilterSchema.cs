using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// Declares the columns a filter expression may reference over <typeparamref name="T"/>. Each column
/// is declared once and carries both sides of the mapping: how to read it from a .NET instance, and
/// the canonical DuckDB column it renders to. Aliases share the declaration, so an expression written
/// with an alias renders as the canonical column name.
/// <para>
/// Column identifiers are case-insensitive, matching SQL. Column values are not.
/// </para>
/// </summary>
/// <typeparam name="T">The type a filter is evaluated against.</typeparam>
public sealed class FilterSchema<T> {
    readonly FrozenDictionary<string, FilterColumn<T>> _columns;
    readonly string?                                   _qualifier;
    readonly string?                                   _trimPrefix;

    FilterSchema(FrozenDictionary<string, FilterColumn<T>> columns, string? qualifier, string? trimPrefix) {
        _columns    = columns;
        _qualifier  = qualifier;
        _trimPrefix = trimPrefix;
    }

    /// <summary>
    /// Builds a schema. The builder is discarded once this returns; the schema is immutable.
    /// </summary>
    public static FilterSchema<T> Create(Action<Builder> configure) {
        var builder = new Builder();
        configure(builder);
        return builder.Build();
    }

    /// <summary>
    /// Parses an expression against this schema.
    /// </summary>
    /// <exception cref="FilterFormatException">The expression is not valid.</exception>
    public Filter<T> Parse(string expression) =>
        !TryParse(expression, out var filter, out var error) ? throw error.ToException() : filter;

    /// <summary>
    /// Parses an expression against this schema, reporting failure instead of throwing.
    /// </summary>
    public bool TryParse(
        string expression,
        [NotNullWhen(true)]  out Filter<T>? filter,
        [NotNullWhen(false)] out FilterParseError? error
    ) {
        if (!FilterParser.TryParse(expression, out var parsed, out error)) {
            filter = null;
            return false;
        }

        filter = new(expression, parsed, this);
        return true;
    }

    /// <summary>
    /// Resolves a column reference to its declaration and the key that survives resolution.
    /// Tries, in order: the name as an alias, a dotted path split right-to-left, then the name
    /// with the configured trim prefix stripped.
    /// </summary>
    internal FilterColumn<T>? Find(string propertyName, string? key, out string? resolvedKey) {
        var name = StripQualifier();

        if (_columns.TryGetValue(name, out var column)) {
            resolvedKey = key;
            return column;
        }

        // A dotted path is only ambiguous when the caller gave no explicit key. "data.address.city"
        // splits right-to-left until a registered prefix appears, leaving the rest as the key.
        if (key is null) {
            for (var i = name.Length - 1; i >= 0; i--) {
                if (name[i] != '.') continue;

                if (_columns.TryGetValue(name[..i], out var prefixed)) {
                    resolvedKey = name[(i + 1)..];
                    return prefixed;
                }
            }
        }

        if (StripTrimPrefix() is { } trimmed)
            return Find(trimmed, key, out resolvedKey);

        resolvedKey = null;
        return null;

        string StripQualifier() =>
            _qualifier is not null && propertyName.StartsWith(_qualifier, StringComparison.OrdinalIgnoreCase)
                ? propertyName[_qualifier.Length..]
                : propertyName;

        string? StripTrimPrefix() =>
            _trimPrefix is not null && name.StartsWith(_trimPrefix, StringComparison.OrdinalIgnoreCase)
                ? name[_trimPrefix.Length..]
                : null;
    }

    /// <summary>
    /// Registers columns before the schema is frozen. The first alias of each column is its
    /// canonical DuckDB column name; the rest are additional spellings that render as it.
    /// </summary>
    public sealed class Builder {
        readonly Dictionary<string, FilterColumn<T>> _columns = new(StringComparer.OrdinalIgnoreCase);

        string? _qualifier;
        string? _trimPrefix;

        /// <summary>
        /// Declares a plain-valued column. <typeparamref name="TValue"/> is inferred, and reading at
        /// that type avoids boxing during comparison.
        /// </summary>
        public Builder Scalar<TValue>(Func<T, TValue> accessor, params string[] aliases) =>
            Add(new ScalarAccessor<T, TValue>(accessor), FilterColumnKind.Scalar, aliases);

        // Keyed columns hold values with no declared type, so they cannot bind a typed comparison and
        // always take the untyped path.

        /// <summary>
        /// Declares a string-keyed bag, reached as <c>column['key']</c> or <c>column-&gt;&gt;'key'</c>.
        /// </summary>
        public Builder Map(Func<T, string?, object?> accessor, params string[] aliases) =>
            Add(new KeyedAccessor<T>(accessor), FilterColumnKind.Map, aliases);

        /// <summary>
        /// Declares a nested JSON document, reached by a dotted path or <c>column-&gt;&gt;'key'</c>.
        /// The accessor receives the path with its segments still dot-separated.
        /// </summary>
        public Builder Json(Func<T, string?, object?> accessor, params string[] aliases) =>
            Add(new KeyedAccessor<T>(accessor), FilterColumnKind.Json, aliases);

        /// <summary>
        /// An optional prefix that qualifies a column reference and is stripped before lookup,
        /// so <c>rec.stream</c> resolves the same column as <c>stream</c>.
        /// </summary>
        public Builder Qualifier(string qualifier) {
            _qualifier = qualifier;
            return this;
        }

        /// <summary>
        /// An optional prefix stripped only after every other lookup has failed.
        /// </summary>
        public Builder TrimPrefix(string prefix) {
            _trimPrefix = prefix;
            return this;
        }

        Builder Add(ColumnAccessor<T> accessor, FilterColumnKind kind, string[] aliases) {
            if (aliases.Length == 0)
                throw new ArgumentException($"A column in FilterSchema<{typeof(T).Name}> needs at least one alias.", nameof(aliases));

            var column = new FilterColumn<T>(aliases[0], kind, accessor);

            foreach (var alias in aliases) {
                if (!_columns.TryAdd(alias, column))
                    throw new ArgumentException($"Duplicate alias '{alias}' in FilterSchema<{typeof(T).Name}>.", nameof(aliases));
            }

            return this;
        }

        internal FilterSchema<T> Build() =>
            new(_columns.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), _qualifier, _trimPrefix);
    }
}
