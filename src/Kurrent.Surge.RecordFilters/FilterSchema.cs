using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

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
        filter = null;

        if (!FilterParser.TryParse(expression, out var parsed, out error)) return false;
        if (!TryValidate(parsed, out error)) return false;

        filter = new(expression, parsed, this);
        return true;
    }

    /// <summary>
    /// Binds a column path to a reader of its text form, for a caller that wants the value rather than
    /// a predicate. The path resolves by the same rules an expression uses.
    /// </summary>
    /// <param name="path">A column reference such as <c>stream</c> or <c>props['region']</c>.</param>
    /// <param name="read">The reader, when the path names a declared column.</param>
    public bool TryBind(string path, [NotNullWhen(true)] out Func<T, string?>? read) {
        read = null;

        if (!FilterParser.TryParsePath(path, out var property, out _)) return false;

        var column = Find(property.PropertyName, property.Key, out var key);
        if (column is null) return false;

        read = column.Accessor.TextReader(key);
        return true;
    }

    // An undeclared column has no answer either side can give: the evaluator would read it as absent on
    // every instance while the renderer refuses to name it. Rejecting here keeps a typo from reading as
    // an expression that simply matches nothing.
    bool TryValidate(IFilterExpression expression, [NotNullWhen(false)] out FilterParseError? error) {
        error = null;

        switch (expression) {
            case AndExpression e: return TryValidate(e.Left, out error) && TryValidate(e.Right, out error);
            case OrExpression e:  return TryValidate(e.Left, out error) && TryValidate(e.Right, out error);
            case NotExpression e: return TryValidate(e.Inner, out error);

            case ComparisonExpression e: error = Declared(e.Property); break;
            case IsNullExpression e:     error = Declared(e.Property); break;
            case LikeExpression e:       error = Declared(e.Property); break;
            case InExpression e:         error = Declared(e.Property); break;
            case BetweenExpression e:    error = Declared(e.Property); break;
        }

        return error is null;

        FilterParseError? Declared(PropertyAccess property) =>
            Find(property.PropertyName, property.Key, out _) is not null
                ? null
                : FilterParseError.UndeclaredColumn(property.PropertyName, typeof(T).Name);
    }

    /// <summary>
    /// Resolves a column reference to its declaration and the key that survives resolution.
    /// Tries, in order: the name as an alias, a dotted path split right-to-left, then the name with
    /// the configured trim prefix stripped.
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

                if (!_columns.TryGetValue(name[..i], out var prefixed)) continue;

                // A dotted name descends, so a JSON column takes the rest as a path. A key written
                // between arrows or brackets does not, which is what keeps `->>'a.b'` a member name.
                var rest = name[(i + 1)..];
                resolvedKey = prefixed.Kind is FilterColumnKind.Json ? $"$.{rest}" : rest;
                return prefixed;
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

        /// <summary>
        /// Declares a plain-valued column with an explicit text form, for when <c>ToString</c> is not
        /// what a caller reading the value back needs. Predicates are unaffected — a text form changes
        /// what <see cref="TryBind"/> returns, never what an expression matches.
        /// </summary>
        public Builder Scalar<TValue>(Func<T, TValue> accessor, Func<TValue, string?> text, params string[] aliases) =>
            Add(new ScalarAccessor<T, TValue>(accessor, text), FilterColumnKind.Scalar, aliases);

        // Keyed columns hold values with no declared type, so they cannot bind a typed comparison and
        // always take the untyped path.

        /// <summary>
        /// Declares a string-keyed bag, reached as <c>column['key']</c> or <c>column-&gt;&gt;'key'</c>.
        /// </summary>
        public Builder Map(Func<T, string?, object?> accessor, params string[] aliases) =>
            Add(new KeyedAccessor<T>(accessor), FilterColumnKind.Map, aliases);

        /// <summary>
        /// Declares a nested JSON document, reached by a dotted path or <c>column-&gt;&gt;'key'</c>.
        /// Supply the document; path navigation belongs to the engine, which also renders the path.
        /// </summary>
        public Builder Json(Func<T, JsonNode?> document, params string[] aliases) =>
            Add(new JsonAccessor<T>(document), FilterColumnKind.Json, aliases);

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
