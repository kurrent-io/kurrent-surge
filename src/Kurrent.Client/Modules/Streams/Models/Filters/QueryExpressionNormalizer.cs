using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using DotNext.Buffers;
using static System.StringComparison;

namespace Kurrent.Client.Streams;

#if NET9_0_OR_GREATER

public interface IQueryExpressionNormalizer {
    NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer);
}

/// <summary>
/// Single-pass, zero-allocation scanner that rewrites SQL-like query expressions by replacing
/// column and table identifiers according to configurable replacement maps.
/// <para>
/// The scanner is context-aware: it uses a <see cref="ScannerState"/> state machine to distinguish
/// column identifiers (in SELECT, WHERE, etc.) from table identifiers (after FROM, JOIN, etc.) and
/// applies the correct replacement dictionary. Identifiers in alias, CTE name, lambda parameter, and
/// cast type positions are copied verbatim to prevent incorrect rewrites.
/// </para>
/// <para>
/// Parenthesized subexpressions are handled via a fixed-depth (32) state stack that pushes the current
/// state on <c>(</c> and pops on <c>)</c>, so nested subqueries each get their own independent state
/// tracking.
/// </para>
/// <para>
/// All SQL literal forms (single-quoted strings, E-strings, dollar-quoted strings, double-quoted
/// identifiers, line/block comments, bracket subscripts, numeric literals with underscore separators,
/// <c>$N</c> parameter references) are recognized and passed through without replacement.
/// </para>
/// </summary>
public sealed class QueryExpressionNormalizer : IQueryExpressionNormalizer {
    static readonly SearchValues<char> IdentifierPartChars =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_");

    static readonly SearchValues<char> NumericChars = SearchValues.Create("0123456789_");

    static readonly FrozenDictionary<string, KeywordKind>.AlternateLookup<ReadOnlySpan<char>> Keywords =
        new Dictionary<string, KeywordKind> {
            // TableIntroducer
            ["FROM"]    = KeywordKind.TableIntroducer,
            ["JOIN"]    = KeywordKind.TableIntroducer,
            ["INTO"]    = KeywordKind.TableIntroducer,
            ["UPDATE"]  = KeywordKind.TableIntroducer,
            ["TABLE"]   = KeywordKind.TableIntroducer,
            ["PIVOT"]   = KeywordKind.TableIntroducer,
            ["UNPIVOT"] = KeywordKind.TableIntroducer,

            // JoinModifier
            ["LEFT"]       = KeywordKind.JoinModifier,
            ["RIGHT"]      = KeywordKind.JoinModifier,
            ["INNER"]      = KeywordKind.JoinModifier,
            ["OUTER"]      = KeywordKind.JoinModifier,
            ["FULL"]       = KeywordKind.JoinModifier,
            ["CROSS"]      = KeywordKind.JoinModifier,
            ["NATURAL"]    = KeywordKind.JoinModifier,
            ["LATERAL"]    = KeywordKind.JoinModifier,
            ["SEMI"]       = KeywordKind.JoinModifier,
            ["ANTI"]       = KeywordKind.JoinModifier,
            ["ASOF"]       = KeywordKind.JoinModifier,
            ["POSITIONAL"] = KeywordKind.JoinModifier,

            // ContextReset
            ["SELECT"]    = KeywordKind.ContextReset,
            ["WHERE"]     = KeywordKind.ContextReset,
            ["ON"]        = KeywordKind.ContextReset,
            ["SET"]       = KeywordKind.ContextReset,
            ["ORDER"]     = KeywordKind.ContextReset,
            ["GROUP"]     = KeywordKind.ContextReset,
            ["HAVING"]    = KeywordKind.ContextReset,
            ["LIMIT"]     = KeywordKind.ContextReset,
            ["UNION"]     = KeywordKind.ContextReset,
            ["VALUES"]    = KeywordKind.ContextReset,
            ["RETURNING"] = KeywordKind.ContextReset,
            ["QUALIFY"]   = KeywordKind.ContextReset,
            ["WINDOW"]    = KeywordKind.ContextReset,
            ["USING"]     = KeywordKind.ContextReset,
            ["OFFSET"]    = KeywordKind.ContextReset,
            ["EXCEPT"]    = KeywordKind.ContextReset,
            ["INTERSECT"] = KeywordKind.ContextReset,
            ["FILTER"]    = KeywordKind.ContextReset,
            ["SAMPLE"]    = KeywordKind.ContextReset,

            // Special
            ["AS"]        = KeywordKind.As,
            ["WITH"]      = KeywordKind.With,
            ["LAMBDA"]    = KeywordKind.Lambda,
            ["RECURSIVE"] = KeywordKind.Recursive,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
         .GetAlternateLookup<ReadOnlySpan<char>>();

    readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _columnLookup;
    readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _tableLookup;

    readonly string? _forbiddenTablePrefix;

    public QueryExpressionNormalizer(NormalizerOptions options) {
        _forbiddenTablePrefix = options.ForbiddenTablePrefix;

        _columnLookup = options.ColumnReplacements
            .ToFrozenDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            .GetAlternateLookup<ReadOnlySpan<char>>();

        _tableLookup = options.TableReplacements
            .ToFrozenDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            .GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer) {
        var w     = new ScanWriter(buffer);
        var state = ScannerState.Default;

        Span<ScannerState> stateStack = stackalloc ScannerState[32];
        var                depth      = 0;

        for (var i = 0; i < query.Length;) {
            var ch = query[i];

            // -- single-line comment
            if (ch == '-' && i + 1 < query.Length && query[i + 1] == '-') {
                SkipLineComment(query, ref i, ref w);
                continue;
            }

            // /* block comment */
            if (ch == '/' && i + 1 < query.Length && query[i + 1] == '*') {
                SkipBlockComment(query, ref i, ref w);
                continue;
            }

            // E'...' or e'...' escape string (must come before identifier scan)
            if (ch is 'E' or 'e' && i + 1 < query.Length && query[i + 1] == '\'') {
                SkipEscapeString(query, ref i, ref w);
                continue;
            }

            // '...' string literal
            if (ch == '\'') {
                SkipStringLiteral(query, ref i, ref w);

                continue;
            }

            // $$...$$ or $tag$...$tag$ dollar-quoted string, or $param reference
            if (ch == '$') {
                // If $ is followed by a digit, it's a parameter reference — copy $ and all digits
                if (i + 1 < query.Length && char.IsDigit(query[i + 1])) {
                    w.Copy(ch);
                    i++;

                    while (i < query.Length && char.IsDigit(query[i])) {
                        w.Copy(query[i]);
                        i++;
                    }

                    continue;
                }

                // Check for $$ or $tag$ (dollar-quoted string)
                if (i + 1 < query.Length && query[i + 1] == '$') {
                    SkipDollarQuotedString(query, ref i, ref w);

                    continue;
                }

                // $ followed by identifier start: could be $tag$ string OR $param
                if (i + 1 < query.Length && (char.IsLetter(query[i + 1]) || query[i + 1] == '_')) {
                    // Look ahead to see if there's a closing $ (dollar-quoted string tag)
                    var tagRest = query[(i + 1)..];
                    var tagEnd  = tagRest.IndexOfAnyExcept(IdentifierPartChars);
                    var j       = i + 1 + (tagEnd < 0 ? tagRest.Length : tagEnd);

                    if (j < query.Length && query[j] == '$') {
                        // This is a $tag$...$tag$ dollar-quoted string
                        SkipDollarQuotedString(query, ref i, ref w);

                        continue;
                    }

                    // No closing $ for tag — it's a $param reference, copy verbatim
                    w.Copy(ch);
                    i++;
                    var paramRest = query[i..];
                    var paramEnd  = paramRest.IndexOfAnyExcept(IdentifierPartChars);
                    var paramLen  = paramEnd < 0 ? paramRest.Length : paramEnd;
                    w.CopySlice(paramRest[..paramLen]);
                    i += paramLen;

                    continue;
                }

                w.Copy(ch);
                i++;

                continue;
            }

            // [...] subscript/list
            if (ch == '[') {
                SkipBracket(query, ref i, ref w);

                continue;
            }

            // "double-quoted identifier"
            if (ch == '"') {
                var error = HandleDoubleQuotedIdentifier(
                    query, ref i, ref w,
                    ref state
                );

                if (error is { })
                    return NormalizeResult.Failure(error);

                continue;
            }

            // bare identifier: scan, classify, and apply state-dependent replacement
            if (IsIdentifierStart(ch)) {
                var start     = i;
                var identRest = query[(i + 1)..];
                var identEnd  = identRest.IndexOfAnyExcept(IdentifierPartChars);

                i += 1 + (identEnd < 0 ? identRest.Length : identEnd);

                var ident   = query[start..i];
                var keyword = ClassifyKeyword(ident);

                switch (state) {
                    case ScannerState.Default: {
                        // Colon-alias lookahead: check if this identifier is followed by `:` (not `::`)
                        if (keyword == KeywordKind.None && PeekColonAlias(query, i, out var colonPos)) {
                            // The identifier is an alias — copy verbatim
                            w.CopySlice(ident);
                            // Copy any whitespace between identifier and colon, then the colon
                            w.CopySlice(query[i..(colonPos + 1)]);
                            i = colonPos + 1;

                            // Next identifier gets column replacement (stay in Default)
                            break;
                        }

                        // Dot-qualified lookahead: qualifier.column — skip qualifier, apply replacement to column
                        if (keyword == KeywordKind.None && i < query.Length && query[i] == '.') {
                            w.CopySlice(ident);
                            w.Copy('.');
                            i++; // skip the dot

                            // Next identifier gets column replacement (stay in Default)
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingTable;

                                break;

                            case KeywordKind.As:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingAlias;

                                break;

                            case KeywordKind.With:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingCTE;

                                break;

                            case KeywordKind.Lambda:
                                w.CopySlice(ident);
                                state = ScannerState.AfterLambda;

                                break;

                            case KeywordKind.None: ApplyColumnReplacement(ident, ref w); break;

                            default: w.CopySlice(ident); break;
                        }

                        break;
                    }

                    case ScannerState.ExpectingTable: {
                        // Colon-alias lookahead: check if this identifier is followed by `:` (not `::`)
                        if (PeekColonAlias(query, i, out var colonPos)) {
                            // The identifier is an alias — copy verbatim
                            w.CopySlice(ident);
                            // Copy any whitespace between identifier and colon, then the colon
                            w.CopySlice(query[i..(colonPos + 1)]);
                            i = colonPos + 1;

                            // Next identifier gets table replacement (stay in ExpectingTable)
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.JoinModifier:
                                w.CopySlice(ident);

                                // stay in ExpectingTable
                                break;

                            default:
                                if (_forbiddenTablePrefix is { } &&
                                    ident.StartsWith(_forbiddenTablePrefix.AsSpan(), OrdinalIgnoreCase))
                                    return NormalizeResult.Failure($"Table '{ident.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'");

                                // Any identifier (keyword or not) in table position gets table replacement
                                ApplyTableReplacement(ident, ref w);
                                state = ScannerState.AfterTable;

                                break;
                        }

                        break;
                    }

                    case ScannerState.AfterTable:
                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                            case KeywordKind.JoinModifier:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingTable;

                                break;

                            case KeywordKind.As:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingTableAlias;

                                break;

                            default:
                                // implicit alias, context reset, or any other keyword
                                w.CopySlice(ident);
                                state = ScannerState.Default;

                                break;
                        }

                        break;

                    case ScannerState.ExpectingQualifiedTable: {
                        // The identifier after schema. is the real table name — prefix check + table replacement
                        if (_forbiddenTablePrefix is { } &&
                            ident.StartsWith(_forbiddenTablePrefix.AsSpan(), OrdinalIgnoreCase))
                            return NormalizeResult.Failure($"Table '{ident.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'");

                        ApplyTableReplacement(ident, ref w);
                        state = ScannerState.AfterTable;

                        break;
                    }

                    case ScannerState.ExpectingAlias:
                    case ScannerState.AfterCast:
                        // Alias after AS or type name after :: — copy verbatim, no replacement
                        w.CopySlice(ident);
                        state = ScannerState.Default;

                        break;

                    case ScannerState.ExpectingTableAlias:
                        // Alias after AS in table context — copy verbatim, return to table context
                        w.CopySlice(ident);
                        state = ScannerState.AfterTable;

                        break;

                    case ScannerState.ExpectingCTE:
                        if (keyword == KeywordKind.Recursive)
                            // RECURSIVE is transparent — stay in ExpectingCTE
                            w.CopySlice(ident);
                        else {
                            // CTE name — copy verbatim, no replacement
                            w.CopySlice(ident);
                            state = ScannerState.AfterCTE;
                        }

                        break;

                    case ScannerState.AfterCTE:
                        w.CopySlice(ident);
                        state = ScannerState.Default;

                        break;

                    case ScannerState.AfterLambda:
                        // Lambda parameter name — copy verbatim, no replacement
                        w.CopySlice(ident);

                        // Stay in AfterLambda (multi-parameter lambda)
                        break;
                }

                continue;
            }

            // :: cast operator
            if (ch == ':' && i + 1 < query.Length && query[i + 1] == ':') {
                w.Copy(':');
                w.Copy(':');
                i     += 2;
                state =  ScannerState.AfterCast;

                continue;
            }

            // : in AfterLambda → end of lambda parameters, transition back to Default
            if (ch == ':' && state == ScannerState.AfterLambda) {
                w.Copy(':');
                i++;
                state = ScannerState.Default;

                continue;
            }

            // dot in AfterTable → schema.table; the identifier already written was the schema name
            if (ch == '.' && state == ScannerState.AfterTable) {
                w.Copy('.');
                i++;
                state = ScannerState.ExpectingQualifiedTable;

                continue;
            }

            // comma in AfterTable → ExpectingTable (multi-table FROM lists)
            if (ch == ',' && state == ScannerState.AfterTable) {
                w.Copy(ch);
                i++;
                state = ScannerState.ExpectingTable;

                continue;
            }

            // comma in AfterCTE → ExpectingCTE (multiple CTEs)
            if (ch == ',' && state == ScannerState.AfterCTE) {
                w.Copy(ch);
                i++;
                state = ScannerState.ExpectingCTE;

                continue;
            }

            // ( push state onto stack, reset to Default
            if (ch == '(') {
                if (depth >= 32)
                    return NormalizeResult.Failure("Nesting depth exceeds maximum of 32");

                stateStack[depth] = state;
                depth++;
                state = ScannerState.Default;
                w.Copy(ch);
                i++;

                continue;
            }

            // ) pop state from stack
            if (ch == ')') {
                if (depth > 0) {
                    depth--;
                    state = stateStack[depth];

                    // If the subquery was in table position, the subquery served as the table
                    if (state == ScannerState.ExpectingTable)
                        state = ScannerState.AfterTable;
                }

                w.Copy(ch);
                i++;

                continue;
            }

            // numeric literal with possible underscore separators (e.g. 100_000)
            if (char.IsAsciiDigit(ch)) {
                var numRest = query[i..];
                var numEnd  = numRest.IndexOfAnyExcept(NumericChars);
                var numLen  = numEnd < 0 ? numRest.Length : numEnd;
                w.CopySlice(numRest[..numLen]);
                i += numLen;

                continue;
            }

            // default: copy character
            w.Copy(ch);
            i++;
        }

        return NormalizeResult.Success(w.Result);
    }

    void ApplyColumnReplacement(ReadOnlySpan<char> ident, ref ScanWriter w) =>
        w.CopySlice(_columnLookup.TryGetValue(ident, out var replacement) ? replacement.AsSpan() : ident);

    void ApplyTableReplacement(ReadOnlySpan<char> ident, ref ScanWriter w) =>
        w.CopySlice(_tableLookup.TryGetValue(ident, out var replacement) ? replacement.AsSpan() : ident);

    static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    static KeywordKind ClassifyKeyword(ReadOnlySpan<char> ident) =>
        Keywords.TryGetValue(ident, out var kind) ? kind : KeywordKind.None;

    /// <summary>
    /// Peeks ahead from <paramref name="pos"/> (just past an identifier) to see if
    /// the next non-whitespace character is ':' NOT followed by another ':'.
    /// Returns true and outputs the position of the ':' if found.
    /// </summary>
    static bool PeekColonAlias(ReadOnlySpan<char> query, int pos, out int colonPos) {
        var j = pos;

        while (j < query.Length && char.IsWhiteSpace(query[j]))
            j++;

        if (j < query.Length && query[j] == ':') {
            // Rule out :: (cast operator)
            if (j + 1 < query.Length && query[j + 1] == ':') {
                colonPos = -1;

                return false;
            }

            colonPos = j;

            return true;
        }

        colonPos = -1;

        return false;
    }

    /// <summary>
    /// Handles a double-quoted identifier at the current position.
    /// Returns null on success, or an error message string on failure (forbidden prefix).
    /// </summary>
    string? HandleDoubleQuotedIdentifier(ReadOnlySpan<char> query, ref int pos, ref ScanWriter w, ref ScannerState state) {
        var start = pos;
        pos++; // skip opening "

        // Scan to closing ", handling "" escapes — vectorized via IndexOf
        var hasEscapes = false;

        while (pos < query.Length) {
            var dqRest = query[pos..];
            var dqHit  = dqRest.IndexOf('"');

            if (dqHit < 0) {
                pos = query.Length;
                break;
            }

            pos += dqHit + 1;

            if (pos < query.Length && query[pos] == '"') {
                hasEscapes = true;
                pos++;
                continue;
            }

            break;
        }

        // Content between the outer quotes (including any "" escapes)
        var rawContent = query[(start + 1)..(pos - 1)];

        // For lookup, unescape "" → " if needed
        ReadOnlySpan<char> lookupContent;

        if (hasEscapes) {
            var unescaped = rawContent.ToString().Replace("\"\"", "\"");
            lookupContent = unescaped.AsSpan();
        }
        else
            lookupContent = rawContent;

        switch (state) {
            case ScannerState.Default: {
                if (_columnLookup.TryGetValue(lookupContent, out var replacement)) {
                    w.Copy('"');
                    w.CopySlice(replacement.AsSpan());
                    w.Copy('"');
                }
                else
                    w.CopySlice(query[start..pos]);

                break;
            }

            case ScannerState.ExpectingTable:
            case ScannerState.ExpectingQualifiedTable: {
                if (_forbiddenTablePrefix is { } &&
                    lookupContent.StartsWith(_forbiddenTablePrefix.AsSpan(), OrdinalIgnoreCase))
                    return $"Table '{lookupContent.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'";

                if (_tableLookup.TryGetValue(lookupContent, out var replacement)) {
                    w.Copy('"');
                    w.CopySlice(replacement.AsSpan());
                    w.Copy('"');
                }
                else
                    w.CopySlice(query[start..pos]);

                state = ScannerState.AfterTable;

                break;
            }

            case ScannerState.ExpectingAlias:
            case ScannerState.AfterTable:
            case ScannerState.AfterCast:
                // Alias, implicit alias, or type name — copy verbatim
                w.CopySlice(query[start..pos]);
                state = ScannerState.Default;

                break;

            case ScannerState.ExpectingTableAlias:
                // Double-quoted alias in table context — copy verbatim, return to table context
                w.CopySlice(query[start..pos]);
                state = ScannerState.AfterTable;

                break;

            default: w.CopySlice(query[start..pos]); break;
        }

        return null;
    }

    /// <summary>
    /// Copies a <c>--</c> single-line comment verbatim through to <c>\n</c> or end of input.
    /// </summary>
    static void SkipLineComment(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('-');
        w.Copy('-');
        pos += 2;
        var rest = q[pos..];
        var nl   = rest.IndexOf('\n');

        if (nl < 0) {
            w.CopySlice(rest);
            pos = q.Length;
        }
        else {
            w.CopySlice(rest[..(nl + 1)]);
            pos += nl + 1;
        }
    }

    /// <summary>
    /// Copies a <c>/* */</c> block comment verbatim, supporting nested <c>/* */</c> pairs.
    /// </summary>
    static void SkipBlockComment(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('/');
        w.Copy('*');
        pos += 2;
        var depth = 1;

        while (pos < q.Length && depth > 0) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny('*', '/');

            if (hit < 0) {
                w.CopySlice(rest);
                pos = q.Length;

                return;
            }

            if (hit > 0) {
                w.CopySlice(rest[..hit]);
                pos += hit;
            }

            if (pos + 1 < q.Length && q[pos] == '/' && q[pos + 1] == '*') {
                w.Copy('/');
                w.Copy('*');
                pos += 2;
                depth++;
            }
            else if (pos + 1 < q.Length && q[pos] == '*' && q[pos + 1] == '/') {
                w.Copy('*');
                w.Copy('/');
                pos += 2;
                depth--;
            }
            else {
                w.Copy(q[pos]);
                pos++;
            }
        }
    }

    /// <summary>
    /// Copies a single-quoted string literal verbatim, treating <c>''</c> as an escaped quote.
    /// </summary>
    static void SkipStringLiteral(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('\'');
        pos++;

        while (pos < q.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOf('\'');

            if (hit < 0) {
                w.CopySlice(rest);
                pos = q.Length;

                return;
            }

            if (hit > 0) {
                w.CopySlice(rest[..hit]);
                pos += hit;
            }

            w.Copy('\'');
            pos++;

            // '' is escaped quote, continue
            if (pos < q.Length && q[pos] == '\'') {
                w.Copy('\'');
                pos++;

                continue;
            }

            // single quote ends the string
            return;
        }
    }

    /// <summary>
    /// Copies a PostgreSQL-style <c>E'...'</c> escape string verbatim, handling both
    /// <c>\'</c> backslash escapes and <c>''</c> doubled-quote escapes.
    /// </summary>
    static void SkipEscapeString(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy(q[pos]); // E or e
        pos++;
        w.Copy('\'');
        pos++;

        while (pos < q.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny('\\', '\'');

            if (hit < 0) {
                w.CopySlice(rest);
                pos = q.Length;

                return;
            }

            if (hit > 0) {
                w.CopySlice(rest[..hit]);
                pos += hit;
            }

            if (q[pos] == '\\' && pos + 1 < q.Length) {
                w.Copy('\\');
                pos++;
                w.Copy(q[pos]);
                pos++;

                continue;
            }

            if (q[pos] == '\'') {
                w.Copy('\'');
                pos++;

                if (pos < q.Length && q[pos] == '\'') {
                    w.Copy('\'');
                    pos++;

                    continue;
                }

                return;
            }

            w.Copy(q[pos]);
            pos++;
        }
    }

    /// <summary>
    /// Copies a dollar-quoted string (<c>$$...$$</c> or <c>$tag$...$tag$</c>) verbatim.
    /// Falls back to copying a bare <c>$</c> if the opening sequence is not a valid delimiter.
    /// </summary>
    static void SkipDollarQuotedString(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        // Find the opening delimiter: $$ or $tag$
        var start = pos;
        pos++; // skip first $

        // scan for tag name (alphanumeric/underscore) until next $
        while (pos < q.Length && q[pos] != '$') {
            var c = q[pos];

            if (char.IsLetterOrDigit(c) || c == '_')
                pos++;
            else {
                // not a valid dollar-quoted string tag, just copy the initial $
                pos = start;
                w.Copy(q[pos]);
                pos++;

                return;
            }
        }

        if (pos >= q.Length) {
            // no closing $ for tag, just copy the initial $
            pos = start;
            w.Copy(q[pos]);
            pos++;

            return;
        }

        pos++; // skip closing $ of delimiter
        var delimiter = q[start..pos];
        w.CopySlice(delimiter);

        // find closing delimiter
        while (pos <= q.Length - delimiter.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOf('$');

            if (hit < 0) {
                w.CopySlice(rest);
                pos = q.Length;

                return;
            }

            if (hit > 0) {
                w.CopySlice(rest[..hit]);
                pos += hit;
            }

            if (pos <= q.Length - delimiter.Length &&
                q.Slice(pos, delimiter.Length).SequenceEqual(delimiter)) {
                w.CopySlice(delimiter);
                pos += delimiter.Length;

                return;
            }

            w.Copy(q[pos]);
            pos++;
        }

        // no closing delimiter found, copy remaining
        w.CopySlice(q[pos..]);
        pos = q.Length;
    }

    /// <summary>
    /// Copies a bracket-delimited expression (<c>[...]</c>) verbatim, supporting nested brackets.
    /// </summary>
    static void SkipBracket(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('[');
        pos++;
        var depth = 1;

        while (pos < q.Length && depth > 0) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny('[', ']');

            if (hit < 0) {
                w.CopySlice(rest);
                pos = q.Length;

                return;
            }

            if (hit > 0) {
                w.CopySlice(rest[..hit]);
                pos += hit;
            }

            if (q[pos] == '[')
                depth++;
            else if (q[pos] == ']')
                depth--;

            w.Copy(q[pos]);
            pos++;
        }
    }
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

/// <summary>
/// Stack-only output cursor that writes normalized query text into a caller-supplied buffer.
/// Tracks a monotonically advancing write position so successive <see cref="Copy"/> and
/// <see cref="CopySlice"/> calls append contiguously. No bounds checking is performed;
/// the caller must ensure the buffer is large enough (at minimum, the length of the input query).
/// </summary>
ref struct ScanWriter(Span<char> buffer) {
    readonly Span<char> _buffer = buffer;

    /// <summary>
    /// Number of characters written so far.
    /// </summary>
    public int Written { get; private set; } = 0;

    /// <summary>
    /// Appends a single character and advances the write position.
    /// </summary>
    public void Copy(char c) => _buffer[Written++] = c;

    /// <summary>
    /// Appends a contiguous span of characters and advances the write position.
    /// </summary>
    public void CopySlice(ReadOnlySpan<char> slice) {
        slice.CopyTo(_buffer[Written..]);
        Written += slice.Length;
    }

    /// <summary>
    /// Returns the portion of the buffer that has been written to.
    /// </summary>
    public ReadOnlySpan<char> Result => _buffer[..Written];
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

#endif
