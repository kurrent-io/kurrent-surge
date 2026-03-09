// ReSharper disable InconsistentNaming

using System.Collections.Immutable;
using DotNext.Buffers;

namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public interface IUtf8QueryExpressionNormalizer {
    Utf8NormalizeResult Normalize(ReadOnlySpan<byte> query, ref BufferWriterSlim<byte> writer);
}

public readonly struct Utf8NormalizeResult {
    public bool    IsValid { get; init; }
    public string? Error   { get; init; }

    public static Utf8NormalizeResult Success() =>
        new() { IsValid = true };

    public static Utf8NormalizeResult Failure(string error) =>
        new() { IsValid = false, Error = error };
}

public interface IQueryExpressionNormalizer {
    NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer);
}

public readonly ref struct NormalizeResult {
    public ReadOnlySpan<char> Value   { get; init; }
    public bool               IsValid { get; init; }
    public string?            Error   { get; init; }

    public static NormalizeResult Success(ReadOnlySpan<char> normalized) =>
        new() {
            Value   = normalized,
            IsValid = true
        };

    public static NormalizeResult Failure(string error) =>
        new() {
            Value   = ReadOnlySpan<char>.Empty,
            IsValid = false,
            Error   = error
        };
}

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

enum KeywordKind {
    /// <summary>
    /// Not a recognized keyword. The identifier is subject to replacement based on the current
    /// scanner state (column replacement in Default, table replacement in ExpectingTable).
    /// </summary>
    None,

    /// <summary>
    /// FROM, JOIN, INTO, UPDATE, TABLE, PIVOT, UNPIVOT. Transitions the scanner to
    /// <see cref="ScannerState.ExpectingTable"/>.
    /// </summary>
    TableIntroducer,

    /// <summary>
    /// LEFT, RIGHT, INNER, OUTER, FULL, CROSS, NATURAL, LATERAL, SEMI, ANTI, ASOF, POSITIONAL.
    /// Transparent in table context; the scanner stays in ExpectingTable waiting for the actual table
    /// name. In Default context, treated as a regular keyword (copied verbatim).
    /// </summary>
    JoinModifier,

    /// <summary>
    /// SELECT, WHERE, ON, SET, ORDER, GROUP, HAVING, LIMIT, UNION, VALUES, RETURNING, QUALIFY,
    /// WINDOW, USING, OFFSET, EXCEPT, INTERSECT, FILTER, SAMPLE. Resets the scanner to
    /// <see cref="ScannerState.Default"/> (column context).
    /// </summary>
    ContextReset,

    /// <summary>
    /// AS keyword. Transitions to <see cref="ScannerState.ExpectingAlias"/> in column context
    /// or <see cref="ScannerState.ExpectingTableAlias"/> in table context.
    /// </summary>
    As,

    /// <summary>
    /// WITH keyword. Transitions to <see cref="ScannerState.ExpectingCTE"/>.
    /// </summary>
    With,

    /// <summary>
    /// LAMBDA keyword. Transitions to <see cref="ScannerState.AfterLambda"/> where identifiers
    /// are treated as parameter names (verbatim) until the <c>:</c> delimiter.
    /// </summary>
    Lambda,

    /// <summary>
    /// RECURSIVE keyword. Transparent inside <see cref="ScannerState.ExpectingCTE"/>; the scanner
    /// stays in that state. In any other context, treated as a regular identifier.
    /// </summary>
    Recursive
}

/// <summary>
/// Tracks the scanner position within the query grammar to determine how the next
/// identifier should be treated: column replacement, table replacement, or verbatim copy.
/// </summary>
enum ScannerState {
    /// <summary>
    /// Column context. Bare identifiers are looked up in the column replacement map.
    /// </summary>
    Default,

    /// <summary>
    /// After a table introducer (FROM, JOIN, INTO, UPDATE, TABLE, PIVOT, UNPIVOT). Next identifier
    /// is looked up in the table replacement map and checked against the forbidden prefix.
    /// </summary>
    ExpectingTable,

    /// <summary>
    /// After consuming a table name. Watching for dot (schema.table), comma (multi-table list),
    /// AS (explicit alias), another table introducer, or a context-resetting keyword.
    /// </summary>
    AfterTable,

    /// <summary>
    /// After a dot in table position (e.g. <c>schema.table</c>). The preceding identifier was the
    /// schema qualifier; the next identifier is the actual table name and gets table replacement.
    /// </summary>
    ExpectingQualifiedTable,

    /// <summary>
    /// After AS in column context. Next identifier is an alias; copied verbatim without
    /// replacement. Transitions back to Default.
    /// </summary>
    ExpectingAlias,

    /// <summary>
    /// After AS in table context (e.g. <c>FROM x AS alias</c>). Next identifier is a table alias;
    /// copied verbatim. Transitions back to AfterTable.
    /// </summary>
    ExpectingTableAlias,

    /// <summary>
    /// After WITH keyword. Next identifier is a CTE name; copied verbatim without replacement.
    /// RECURSIVE is transparent and keeps the state in ExpectingCTE.
    /// </summary>
    ExpectingCTE,

    /// <summary>
    /// After a CTE name has been consumed. Watching for comma (another CTE definition) or any
    /// other token which transitions to Default for the CTE body.
    /// </summary>
    AfterCTE,

    /// <summary>
    /// After the LAMBDA keyword. All following identifiers until <c>:</c> are lambda parameter
    /// names; copied verbatim. The colon ends the parameter list and transitions back to Default.
    /// </summary>
    AfterLambda,

    /// <summary>
    /// After the <c>::</c> cast operator. Next identifier is a type name (e.g. <c>::int</c>);
    /// copied verbatim without replacement. Transitions back to Default.
    /// </summary>
    AfterCast
}

#endif
