using System.Text.Json.Nodes;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// How a column carries its value, on both sides of the mapping. The kind decides how a key is
/// applied when reading in .NET, and which DuckDB access form the column renders to.
/// </summary>
enum FilterColumnKind {
    /// <summary>
    /// A plain value. Takes no key. Renders as the bare column name.
    /// </summary>
    Scalar,

    /// <summary>
    /// A string-keyed bag. Renders as <c>column-&gt;&gt;'key'</c>.
    /// </summary>
    Map,

    /// <summary>
    /// A nested JSON document. Renders as <c>column-&gt;&gt;'$.path'</c>, so a dotted path
    /// reaches nested members through a single JSONPath expression.
    /// </summary>
    Json
}

/// <summary>
/// The SQL type family a value presents as. Comparison legality is decided per family, matching
/// DuckDB: a number or boolean literal binds only against a numeric or boolean column, while a string
/// literal binds against anything and converts where it must.
/// </summary>
enum SqlTypeFamily {
    Text,
    Number,
    Boolean,
    Blob,
    Timestamp,

    /// <summary>
    /// No declared type, so nothing can be ruled out ahead of time.
    /// </summary>
    Unknown
}

static class SqlTypes {
    public static SqlTypeFamily Of(Type type) {
        // Checked before the TypeCode switch: an enum reports its underlying integer there, but it
        // reads and renders as its name, which is a VARCHAR column on the database side.
        if (type.IsEnum) return SqlTypeFamily.Text;

        if (Nullable.GetUnderlyingType(type) is { } nullable) return Of(nullable);

        return Type.GetTypeCode(type) switch {
            TypeCode.String                                                                    => SqlTypeFamily.Text,
            TypeCode.Boolean                                                                   => SqlTypeFamily.Boolean,
            TypeCode.DateTime                                                                  => SqlTypeFamily.Timestamp,
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64 or
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal                             => SqlTypeFamily.Number,
            _ when type == typeof(byte[])                                                      => SqlTypeFamily.Blob,
            _ when type == typeof(DateTimeOffset)                                              => SqlTypeFamily.Timestamp,
            _ when type == typeof(Guid)                                                        => SqlTypeFamily.Text,
            _                                                                                  => SqlTypeFamily.Unknown
        };
    }

    /// <summary>
    /// The family a cast target names, or <see cref="SqlTypeFamily.Unknown"/> when it names nothing.
    /// </summary>
    public static SqlTypeFamily OfTarget(string target) =>
        target.ToUpperInvariant() switch {
            "VARCHAR" or "TEXT" or "STRING"                     => SqlTypeFamily.Text,
            "BIGINT" or "INTEGER" or "INT" or "DOUBLE" or "DECIMAL" => SqlTypeFamily.Number,
            "BOOLEAN" or "BOOL"                                 => SqlTypeFamily.Boolean,
            "UUID"                                              => SqlTypeFamily.Text,
            "BLOB"                                              => SqlTypeFamily.Blob,
            "TIMESTAMP" or "DATE"                               => SqlTypeFamily.Timestamp,
            _                                                   => SqlTypeFamily.Unknown
        };
}

/// <summary>
/// One declared column: the .NET accessor, the canonical DuckDB column name, and the access form
/// shared by both. Aliases resolve to the same instance, which is what lets an alias render as its
/// canonical name.
/// </summary>
sealed class FilterColumn<T>(string name, FilterColumnKind kind, ColumnAccessor<T> accessor) {
    public string            Name     { get; } = name;
    public FilterColumnKind  Kind     { get; } = kind;
    public ColumnAccessor<T> Accessor { get; } = accessor;

    public bool AcceptsKey => Kind is not FilterColumnKind.Scalar;
}

/// <summary>
/// Reads a column from an instance, and — when it knows the value's type — binds a predicate over it
/// ahead of time.
/// <para>
/// <see cref="Getter"/> is the fallback every column supports: it costs a box for a value type and
/// leaves the comparison to be resolved by inspecting the boxed value. The <c>Bind</c> members exist
/// to avoid that. They return <c>null</c> when this accessor cannot specialise for the given literal,
/// which sends the caller back to the fallback rather than changing what the expression means.
/// </para>
/// </summary>
abstract class ColumnAccessor<T> {
    /// <summary>
    /// The SQL type the column presents as, which decides which literals may compare against it.
    /// </summary>
    public abstract SqlTypeFamily SqlType { get; }

    /// <summary>
    /// Reads the column, boxing value types. <paramref name="key"/> is applied by keyed columns and
    /// ignored by scalars.
    /// </summary>
    public abstract Func<T, object?> Getter(string? key);

    /// <summary>
    /// Reads the column as text, for a caller that wants the value itself rather than a predicate over
    /// it. Predicates never take this path — <c>LIKE</c> keeps reading the value directly — so a
    /// declared text form cannot change what an expression matches.
    /// </summary>
    public abstract Func<T, string?> TextReader(string? key);

    public virtual Func<T, bool?>? BindComparison(ComparisonOperator op, FilterValue literal) => null;

    public virtual Func<T, bool?>? BindRange(FilterValue lower, FilterValue upper, bool negated) => null;
}

/// <summary>
/// Holds the accessor at its declared return type, so a predicate can be bound directly against
/// <typeparamref name="TValue"/> with no boxing and no per-record type inspection.
/// </summary>
sealed class ScalarAccessor<T, TValue>(Func<T, TValue> getter, Func<TValue, string?>? text = null) : ColumnAccessor<T> {
    public override SqlTypeFamily SqlType { get; } = SqlTypes.Of(typeof(TValue));

    // Comparer<string>.Default is culture-sensitive. Column values compare ordinally, matching the
    // database, so string columns must not take the default comparer. A byte[] has no default
    // comparer at all — Comparer<byte[]>.Default throws when asked — and DuckDB orders BLOB
    // byte-wise, so it gets one of its own.
    static readonly IComparer<TValue> Comparer =
        typeof(TValue) == typeof(string) ? (IComparer<TValue>)(object)StringComparer.Ordinal
      : typeof(TValue) == typeof(byte[]) ? (IComparer<TValue>)(object)ByteSequenceComparer.Instance
      : Comparer<TValue>.Default;

    public override Func<T, object?> Getter(string? key) {
        var get = getter;
        return t => get(t);
    }

    public override Func<T, string?> TextReader(string? key) {
        var get     = getter;
        var project = text;

        return project is null
            ? t => ColumnText.Of(get(t))
            : t => project(get(t));
    }

    public override Func<T, bool?>? BindComparison(ComparisonOperator op, FilterValue literal) {
        if (!TryCoerce(literal, out var target)) return null;

        var get      = getter;
        var comparer = Comparer;

        return op switch {
            ComparisonOperator.Equal              => t => Compare(t) is { } c ? c == 0 : null,
            ComparisonOperator.NotEqual           => t => Compare(t) is { } c ? c != 0 : null,
            ComparisonOperator.GreaterThan        => t => Compare(t) is { } c ? c > 0  : null,
            ComparisonOperator.LessThan           => t => Compare(t) is { } c ? c < 0  : null,
            ComparisonOperator.GreaterThanOrEqual => t => Compare(t) is { } c ? c >= 0 : null,
            ComparisonOperator.LessThanOrEqual    => t => Compare(t) is { } c ? c <= 0 : null,
            _                                     => null
        };

        // `v is null` folds away for a non-nullable TValue, so value-typed columns pay nothing for it.
        int? Compare(T instance) {
            var v = get(instance);
            return v is null ? null : comparer.Compare(v, target);
        }
    }

    public override Func<T, bool?>? BindRange(FilterValue lower, FilterValue upper, bool negated) {
        if (!TryCoerce(lower, out var low) || !TryCoerce(upper, out var high)) return null;

        var get      = getter;
        var comparer = Comparer;

        return t => {
            var v = get(t);
            if (v is null) return null;

            var within = comparer.Compare(v, low) >= 0 && comparer.Compare(v, high) <= 0;
            return negated ? !within : within;
        };
    }

    /// <summary>
    /// Converts a literal to <typeparamref name="TValue"/>. Returns <c>false</c> when the conversion
    /// would change what the comparison means — a non-integral literal against an integer column, a
    /// value out of range, or a literal of an unrelated kind — leaving the caller on the fallback path
    /// where cross-type comparison is handled.
    /// </summary>
    static bool TryCoerce(FilterValue literal, out TValue result) {
        result = default!;

        switch (literal) {
            case StringFilterValue s when typeof(TValue) == typeof(string):
                result = (TValue)(object)s.Value;
                return true;

            case BooleanFilterValue b when typeof(TValue) == typeof(bool):
                result = (TValue)(object)b.Value;
                return true;

            case BlobFilterValue b when typeof(TValue) == typeof(byte[]):
                result = (TValue)(object)b.Value;
                return true;

            // A BLOB compares against a VARCHAR by the text's own bytes. Encoding here binds the
            // literal once; leaving it to the fallback would encode the same string per record.
            case StringFilterValue s when typeof(TValue) == typeof(byte[]):
                result = (TValue)(object)System.Text.Encoding.UTF8.GetBytes(s.Value);
                return true;

            case NumberFilterValue n:
                return TryCoerceNumber(n.Value, out result);

            default:
                return false;
        }
    }

    static bool TryCoerceNumber(decimal value, out TValue result) {
        result = default!;

        if (typeof(TValue) == typeof(decimal)) {
            result = (TValue)(object)value;
            return true;
        }

        if (typeof(TValue) == typeof(double)) {
            result = (TValue)(object)(double)value;
            return true;
        }

        // An integer column only takes an integral literal in range. `revision = 150.5` must not
        // truncate into a match against 150 — it falls back and compares as decimal instead.
        if (value != decimal.Truncate(value)) return false;

        if (typeof(TValue) == typeof(long)) {
            if (value < long.MinValue || value > long.MaxValue) return false;
            result = (TValue)(object)(long)value;
            return true;
        }

        if (typeof(TValue) == typeof(int)) {
            if (value < int.MinValue || value > int.MaxValue) return false;
            result = (TValue)(object)(int)value;
            return true;
        }

        return false;
    }
}

/// <summary>
/// A column whose values have no declared type — a metadata bag or a JSON document. It cannot bind
/// ahead of time, so it always takes the fallback path.
/// </summary>
/// <summary>
/// A JSON document. The engine owns what a path means so the .NET walk and the
/// <c>-&gt;&gt;'$.path'</c> the renderer emits cannot disagree; the declarer supplies only the document.
/// </summary>
sealed class JsonAccessor<T>(Func<T, JsonNode?> document) : ColumnAccessor<T> {
    // `->>` yields VARCHAR whatever the document holds, so a number literal cannot bind against it.
    public override SqlTypeFamily SqlType => SqlTypeFamily.Text;

    public override Func<T, object?> Getter(string? key) {
        var read = document;
        return t => Read(read(t), key);
    }

    public override Func<T, string?> TextReader(string? key) {
        var read = document;
        return t => Read(read(t), key);
    }

    static string? Read(JsonNode? node, string? key) {
        if (node is null) return null;

        // A null key means the column itself was referenced — `data IS NULL`, not `data->>'city'`.
        // The answer is the whole document, matching what the column holds.
        if (key is null) return node.ToJsonString();

        var found = Walk(node, key);

        return found switch {
            JsonValue value => value.ToString(),
            _               => found?.ToJsonString()
        };
    }

    // `->>` reads one of three path forms and nothing else: a JSONPath rooted at `$`, an RFC 6901
    // pointer rooted at `/`, or a single member name. A name is never split, so `->>'a.b'` reads a
    // member literally called `a.b` — DuckDB returns NULL there rather than descending.
    static JsonNode? Walk(JsonNode node, string key) =>
        key.StartsWith('$') ? WalkPath(node, key)
      : key.StartsWith('/') ? WalkPointer(node, key)
      : Member(node, key);

    static JsonNode? WalkPath(JsonNode? node, string path) {
        var i = 1; // past the '$'

        while (node is not null && i < path.Length) {
            int start;

            switch (path[i]) {
                case '.':
                    start = ++i;
                    while (i < path.Length && path[i] is not ('.' or '[')) i++;
                    node = Member(node, path[start..i]);
                    break;

                case '[':
                    start = ++i;
                    while (i < path.Length && path[i] != ']') i++;
                    if (!int.TryParse(path[start..i], out var index)) return null;
                    node = Index(node, index);
                    i++; // past the ']'
                    break;

                default:
                    return null;
            }
        }

        return node;
    }

    static JsonNode? WalkPointer(JsonNode? node, string pointer) {
        foreach (var raw in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
            // RFC 6901 escapes: `~1` is a literal '/', `~0` a literal '~'. Tilde is unescaped last
            // so that `~01` reads as the two characters `~1` rather than as a slash.
            var segment = raw.Replace("~1", "/").Replace("~0", "~");

            node = node is JsonArray && int.TryParse(segment, out var index)
                ? Index(node, index)
                : Member(node, segment);

            if (node is null) return null;
        }

        return node;
    }

    static JsonNode? Member(JsonNode? node, string name) =>
        node is JsonObject obj && obj.TryGetPropertyValue(name, out var value) ? value : null;

    // JSON indexing is 0-based, unlike DuckDB's LIST and ARRAY types.
    static JsonNode? Index(JsonNode? node, int index) =>
        node is JsonArray array && index >= 0 && index < array.Count ? array[index] : null;
}

sealed class KeyedAccessor<T>(Func<T, string?, object?> getter) : ColumnAccessor<T> {
    public override SqlTypeFamily SqlType => SqlTypeFamily.Text;

    public override Func<T, object?> Getter(string? key) {
        var get = getter;
        return t => get(t, key);
    }

    public override Func<T, string?> TextReader(string? key) {
        var get = getter;
        return t => ColumnText.Of(get(t, key));
    }
}

sealed class ByteSequenceComparer : IComparer<byte[]> {
    public static readonly ByteSequenceComparer Instance = new();

    public int Compare(byte[]? left, byte[]? right) =>
        (left, right) switch {
            (null, null) => 0,
            (null, _)    => -1,
            (_, null)    => 1,
            _            => left.AsSpan().SequenceCompareTo(right)
        };
}

/// <summary>
/// The text form of a value when no column declared one. A key derived from it must not depend on the
/// type the value happened to arrive as, so the formats are the ones that read back identically.
/// </summary>
static class ColumnText {
    public static string? Of(object? value) =>
        value switch {
            null          => null,
            string text   => text,
            Guid guid     => guid.ToString("N"),
            DateTime date => date.ToString("O"),
            // Matches hex(), the form a BLOB is displayed and round-tripped as.
            byte[] bytes  => Convert.ToHexString(bytes),
            _             => value.ToString()
        };
}
