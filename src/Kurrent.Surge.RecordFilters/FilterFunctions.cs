using System.Collections.Frozen;
using System.Globalization;

namespace Kurrent.Surge.RecordFilters;

/// <summary>
/// The SQL calls an expression may apply to a column. Each entry mirrors DuckDB's own function, so an
/// expression evaluated here and the SQL it renders to select the same rows.
/// </summary>
/// <remarks>
/// Every function propagates NULL: a NULL argument yields NULL without the body running, which is what
/// DuckDB does and what keeps the surrounding predicate UNKNOWN rather than false.
/// </remarks>
static class FilterFunctions {
    /// <summary>
    /// A resolved function body. The compiler looks one up once and closes over it, so no record ever
    /// pays for a lookup by name.
    /// </summary>
    internal delegate object? Implementation(object? value, IReadOnlyList<object?> arguments);

    static readonly FrozenDictionary<string, Implementation> Registry =
        new Dictionary<string, Implementation>(StringComparer.OrdinalIgnoreCase) {
            ["lower"]       = (v, _) => Text(v)?.ToLowerInvariant(),
            ["upper"]       = (v, _) => Text(v)?.ToUpperInvariant(),
            ["trim"]        = (v, _) => Text(v)?.Trim(),
            ["ltrim"]       = (v, _) => Text(v)?.TrimStart(),
            ["rtrim"]       = (v, _) => Text(v)?.TrimEnd(),
            ["length"]      = (v, _) => Text(v) is { } s ? (long)s.Length : null,
            ["starts_with"] = (v, a) => Pair(v, a, (s, p) => s.StartsWith(p, StringComparison.Ordinal)),
            ["ends_with"]   = (v, a) => Pair(v, a, (s, p) => s.EndsWith(p, StringComparison.Ordinal)),
            ["contains"]    = (v, a) => Pair(v, a, (s, p) => s.Contains(p, StringComparison.Ordinal)),
            ["hex"]         = (v, _) => Hex(v),
            ["from_hex"]    = (v, _) => FromHex(v),
            ["epoch_ms"]    = (v, _) => EpochMilliseconds(v),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    static readonly FrozenDictionary<string, SqlTypeFamily> ReturnTypes =
        new Dictionary<string, SqlTypeFamily>(StringComparer.OrdinalIgnoreCase) {
            ["lower"]       = SqlTypeFamily.Text,
            ["upper"]       = SqlTypeFamily.Text,
            ["trim"]        = SqlTypeFamily.Text,
            ["ltrim"]       = SqlTypeFamily.Text,
            ["rtrim"]       = SqlTypeFamily.Text,
            ["length"]      = SqlTypeFamily.Number,
            ["starts_with"] = SqlTypeFamily.Boolean,
            ["ends_with"]   = SqlTypeFamily.Boolean,
            ["contains"]    = SqlTypeFamily.Boolean,
            ["hex"]         = SqlTypeFamily.Text,
            ["from_hex"]    = SqlTypeFamily.Blob,
            ["epoch_ms"]    = SqlTypeFamily.Number,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string name) => Registry.ContainsKey(name);

    /// <summary>
    /// The body behind a name, resolved once at compile time.
    /// </summary>
    public static Implementation Resolve(string name) =>
        Registry.TryGetValue(name, out var implementation)
            ? implementation
            : throw new InvalidOperationException($"Unknown filter function '{name}'.");

    public static SqlTypeFamily ReturnType(string name) =>
        ReturnTypes.TryGetValue(name, out var family) ? family : SqlTypeFamily.Unknown;

    public static object? Apply(string name, object? value, IReadOnlyList<object?> arguments) {
        // NULL in, NULL out — checked before dispatch so no implementation has to repeat it.
        if (value is null) return null;

        return Registry.TryGetValue(name, out var implementation)
            ? implementation(value, arguments)
            : throw new InvalidOperationException($"Unknown filter function '{name}'.");
    }

    #region ->> Casts <<-

    static readonly FrozenSet<string> CastTargets =
        new[] { "VARCHAR", "TEXT", "STRING", "BIGINT", "INTEGER", "INT", "DOUBLE", "DECIMAL", "BOOLEAN", "BOOL", "UUID", "BLOB", "TIMESTAMP", "DATE" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsCastTarget(string target) => CastTargets.Contains(target);

    /// <summary>
    /// The exact conversion a target names. Resolved once so a cast never re-parses its target, and
    /// finer than <see cref="SqlTypeFamily"/> because BIGINT and DOUBLE convert differently.
    /// </summary>
    internal enum CastTarget { Varchar, BigInt, Double, Boolean, Uuid, Blob, Timestamp, Unknown }

    internal static CastTarget ResolveTarget(string target) =>
        target.ToUpperInvariant() switch {
            "VARCHAR" or "TEXT" or "STRING" => CastTarget.Varchar,
            "BIGINT" or "INTEGER" or "INT"  => CastTarget.BigInt,
            "DOUBLE" or "DECIMAL"           => CastTarget.Double,
            "BOOLEAN" or "BOOL"             => CastTarget.Boolean,
            "UUID"                          => CastTarget.Uuid,
            "BLOB"                          => CastTarget.Blob,
            "TIMESTAMP" or "DATE"           => CastTarget.Timestamp,
            _                               => CastTarget.Unknown
        };

    /// <summary>
    /// Converts a value the way DuckDB's <c>CAST</c> does. Returns <c>false</c> when the conversion
    /// fails, which the caller reports as a conversion error rather than a silent non-match.
    /// </summary>
    public static bool TryCast(object? value, string target, out object? result) =>
        TryCast(value, ResolveTarget(target), out result);

    internal static bool TryCast(object? value, CastTarget target, out object? result) {
        result = null;

        if (value is null) return true;

        switch (target) {
            case CastTarget.Varchar:
                result = TextForm(value);
                return result is not null;

            case CastTarget.BigInt:
                // DuckDB rounds a fractional value half away from zero rather than truncating it:
                // CAST(-25.50 AS BIGINT) is -26, and CAST(2.5 AS BIGINT) is 3.
                if (!TryNumber(value, out var integral)) return false;

                var rounded = decimal.Round(integral, 0, MidpointRounding.AwayFromZero);
                if (rounded < long.MinValue || rounded > long.MaxValue) return false;

                result = (long)rounded;
                return true;

            case CastTarget.Double:
                if (!TryNumber(value, out var fractional)) return false;
                result = fractional;
                return true;

            case CastTarget.Boolean:
                switch (value) {
                    case bool flag: result = flag; return true;
                    case string text when bool.TryParse(text, out var parsed): result = parsed; return true;
                    default: return false;
                }

            case CastTarget.Uuid:
                return TryUuid(value, out result);

            case CastTarget.Blob:
                switch (value) {
                    case byte[] bytes: result = bytes; return true;
                    // DuckDB reads a VARCHAR as its raw bytes, not as hex.
                    case string text: result = System.Text.Encoding.UTF8.GetBytes(text); return true;
                    default: return false;
                }

            case CastTarget.Timestamp:
                switch (value) {
                    case DateTime date: result = date; return true;
                    case DateTimeOffset offset: result = offset.UtcDateTime; return true;
                    case string text when DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed):
                        result = parsed;
                        return true;
                    default: return false;
                }

            default:
                return false;
        }
    }

    // DuckDB stores a UUID big-endian, so a 16-byte blob reads in written order. The Guid(byte[])
    // constructor assumes the little-endian layout Windows uses for its first three fields, which
    // would transpose them.
    static bool TryUuid(object value, out object? result) {
        switch (value) {
            case Guid guid:
                result = guid;
                return true;

            case byte[] { Length: 16 } bytes:
                result = new Guid(bytes, bigEndian: true);
                return true;

            case string text when Guid.TryParse(text, out var parsed):
                result = parsed;
                return true;

            default:
                result = null;
                return false;
        }
    }

    #endregion

    #region ->> Value forms <<-

    static string? Text(object? value) => value as string ?? value?.ToString();

    /// <summary>
    /// The text DuckDB shows for a value. Only the forms a <c>CAST … AS VARCHAR</c> is defined for;
    /// anything else returns null and the cast fails.
    /// </summary>
    static string? TextForm(object value) =>
        value switch {
            string text     => text,
            Guid guid       => guid.ToString("D"),
            bool flag       => flag ? "true" : "false",
            long integral   => integral.ToString(CultureInfo.InvariantCulture),
            int integral    => integral.ToString(CultureInfo.InvariantCulture),
            decimal number  => number.ToString(CultureInfo.InvariantCulture),
            double number   => number.ToString(CultureInfo.InvariantCulture),
            DateTime date   => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            byte[] bytes    => BlobText(bytes),
            _               => null
        };

    // DuckDB shows a BLOB as its printable bytes, escaping everything else as \xNN. Printable is
    // 0x20..0x7E, and a backslash escapes itself so the form round-trips.
    static string BlobText(byte[] bytes) {
        var builder = new System.Text.StringBuilder(bytes.Length);

        foreach (var value in bytes) {
            if (value is >= 0x20 and <= 0x7E && value != (byte)'\\')
                builder.Append((char)value);
            else
                builder.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    static string? Hex(object? value) =>
        value switch {
            byte[] bytes => Convert.ToHexString(bytes),
            string text  => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text)),
            _            => null
        };

    static object? FromHex(object? value) {
        if (value is not string text) return null;

        // DuckDB returns NULL for text that is not hex rather than raising, so a bad pair here has to
        // read as NULL too.
        try {
            return Convert.FromHexString(text);
        }
        catch (FormatException) {
            return null;
        }
    }

    // DuckDB's epoch_ms takes a TIMESTAMP and returns milliseconds since the epoch.
    static object? EpochMilliseconds(object? value) =>
        value switch {
            DateTime date         => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            DateTimeOffset offset => offset.ToUnixTimeMilliseconds(),
            _                     => null
        };

    static object? Pair(object? value, IReadOnlyList<object?> arguments, Func<string, string, bool> compare) =>
        Text(value) is { } left && arguments.Count == 1 && Text(arguments[0]) is { } right
            ? compare(left, right)
            : null;

    static bool TryNumber(object value, out decimal result) {
        switch (value) {
            case decimal number:                                    result = number;         return true;
            case long integral:                                     result = integral;       return true;
            case int integral:                                      result = integral;       return true;
            case double number:                                     result = (decimal)number; return true;
            case string text when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                                                                    result = parsed;         return true;
            default:                                                result = 0;              return false;
        }
    }

    #endregion
}
