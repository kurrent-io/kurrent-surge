using System.Buffers;
using System.Text;
using DotNext.Buffers;

namespace Kurrent.Client.Streams;

/// <summary>
/// V2: Linear-scan lookup (Utf8ReplacementLookup) + direct BufferWriterSlim writes (no double-buffering).
/// </summary>
public sealed class Utf8QueryExpressionNormalizerV2 : IUtf8QueryExpressionNormalizer {
    static readonly SearchValues<byte> IdentifierPartBytes =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_"u8);

    static readonly SearchValues<byte> NumericBytes = SearchValues.Create("0123456789_"u8);

    readonly Utf8ReplacementLookup _columnLookup;
    readonly Utf8ReplacementLookup _tableLookup;
    readonly byte[]? _forbiddenTablePrefix;

    public Utf8QueryExpressionNormalizerV2(NormalizerOptions options) {
        _forbiddenTablePrefix = options.ForbiddenTablePrefix is { } prefix
            ? Encoding.UTF8.GetBytes(prefix)
            : null;

        _columnLookup = new(options.ColumnReplacements);
        _tableLookup  = new(options.TableReplacements);
    }

    public void Normalize(ReadOnlySpan<byte> query, ref BufferWriterSlim<byte> writer) {
        var state = ScannerState.Default;

        Span<ScannerState> stateStack = stackalloc ScannerState[32];

        var depth = 0;

        for (var i = 0; i < query.Length;) {
            var ch = query[i];

            // -- single-line comment
            if (ch == (byte)'-' && i + 1 < query.Length && query[i + 1] == (byte)'-') {
                SkipLineComment(query, ref i, ref writer);
                continue;
            }

            // /* block comment */
            if (ch == (byte)'/' && i + 1 < query.Length && query[i + 1] == (byte)'*') {
                SkipBlockComment(query, ref i, ref writer);
                continue;
            }

            // E'...' or e'...' escape string (must come before identifier scan)
            if (ch is (byte)'E' or (byte)'e' && i + 1 < query.Length && query[i + 1] == (byte)'\'') {
                SkipEscapeString(query, ref i, ref writer);
                continue;
            }

            // '...' string literal
            if (ch == (byte)'\'') {
                SkipStringLiteral(query, ref i, ref writer);
                continue;
            }

            // $$...$$ or $tag$...$tag$ dollar-quoted string, or $param reference
            if (ch == (byte)'$') {
                // If $ is followed by a digit, it's a parameter reference — copy $ and all digits
                if (i + 1 < query.Length && query[i + 1] >= (byte)'0' && query[i + 1] <= (byte)'9') {
                    writer.Add(ch);
                    i++;

                    while (i < query.Length && query[i] >= (byte)'0' && query[i] <= (byte)'9') {
                        writer.Add(query[i]);
                        i++;
                    }

                    continue;
                }

                // Check for $$ or $tag$ (dollar-quoted string)
                if (i + 1 < query.Length && query[i + 1] == (byte)'$') {
                    SkipDollarQuotedString(query, ref i, ref writer);
                    continue;
                }

                // $ followed by identifier start: could be $tag$ string OR $param
                if (i + 1 < query.Length && (IsAsciiLetter(query[i + 1]) || query[i + 1] == (byte)'_')) {
                    // Look ahead to see if there's a closing $ (dollar-quoted string tag)
                    var tagRest = query[(i + 1)..];
                    var tagEnd  = tagRest.IndexOfAnyExcept(IdentifierPartBytes);
                    var j       = i + 1 + (tagEnd < 0 ? tagRest.Length : tagEnd);

                    if (j < query.Length && query[j] == (byte)'$') {
                        // This is a $tag$...$tag$ dollar-quoted string
                        SkipDollarQuotedString(query, ref i, ref writer);
                        continue;
                    }

                    // No closing $ for tag — it's a $param reference, copy verbatim
                    writer.Add(ch);
                    i++;
                    var paramRest = query[i..];
                    var paramEnd  = paramRest.IndexOfAnyExcept(IdentifierPartBytes);
                    var paramLen  = paramEnd < 0 ? paramRest.Length : paramEnd;
                    writer.Write(paramRest[..paramLen]);
                    i += paramLen;

                    continue;
                }

                writer.Add(ch);
                i++;
                continue;
            }

            // [...] subscript/list
            if (ch == (byte)'[') {
                SkipBracket(query, ref i, ref writer);
                continue;
            }

            // "double-quoted identifier"
            if (ch == (byte)'"') {
                HandleDoubleQuotedIdentifier(query, ref i, ref writer, ref state);
                continue;
            }

            // bare identifier: scan, classify, and apply state-dependent replacement
            if (IsIdentifierStart(ch)) {
                var start     = i;
                var identRest = query[(i + 1)..];
                var identEnd  = identRest.IndexOfAnyExcept(IdentifierPartBytes);

                i += 1 + (identEnd < 0 ? identRest.Length : identEnd);

                var ident   = query[start..i];
                var keyword = ClassifyKeyword(ident);

                switch (state) {
                    case ScannerState.Default: {
                        // Colon-alias lookahead: check if this identifier is followed by `:` (not `::`)
                        if (keyword == KeywordKind.None && PeekColonAlias(query, i, out var colonPos)) {
                            // The identifier is an alias — copy verbatim
                            writer.Write(ident);
                            // Copy any whitespace between identifier and colon, then the colon
                            writer.Write(query[i..(colonPos + 1)]);
                            i = colonPos + 1;

                            // Next identifier gets column replacement (stay in Default)
                            break;
                        }

                        // Dot-qualified lookahead: qualifier.column — skip qualifier, apply replacement to column
                        if (keyword == KeywordKind.None && i < query.Length && query[i] == (byte)'.') {
                            writer.Write(ident);
                            writer.Add((byte)'.');
                            i++; // skip the dot

                            // Next identifier gets column replacement (stay in Default)
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                                writer.Write(ident);
                                state = ScannerState.ExpectingTable;
                                break;

                            case KeywordKind.As:
                                writer.Write(ident);
                                state = ScannerState.ExpectingAlias;
                                break;

                            case KeywordKind.With:
                                writer.Write(ident);
                                state = ScannerState.ExpectingCTE;
                                break;

                            case KeywordKind.Lambda:
                                writer.Write(ident);
                                state = ScannerState.AfterLambda;
                                break;

                            case KeywordKind.None: ApplyColumnReplacement(ident, ref writer); break;

                            default: writer.Write(ident); break;
                        }

                        break;
                    }

                    case ScannerState.ExpectingTable: {
                        // Colon-alias lookahead: check if this identifier is followed by `:` (not `::`)
                        if (PeekColonAlias(query, i, out var colonPos)) {
                            // The identifier is an alias — copy verbatim
                            writer.Write(ident);
                            // Copy any whitespace between identifier and colon, then the colon
                            writer.Write(query[i..(colonPos + 1)]);
                            i = colonPos + 1;

                            // Next identifier gets table replacement (stay in ExpectingTable)
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.JoinModifier:
                                writer.Write(ident);
                                // stay in ExpectingTable
                                break;

                            default:
                                if (_forbiddenTablePrefix is { } &&
                                    ident.Length >= _forbiddenTablePrefix.Length &&
                                    Ascii.EqualsIgnoreCase(ident[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                                    throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(ident)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                                // Any identifier (keyword or not) in table position gets table replacement
                                ApplyTableReplacement(ident, ref writer);
                                state = ScannerState.AfterTable;
                                break;
                        }

                        break;
                    }

                    case ScannerState.AfterTable:
                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                            case KeywordKind.JoinModifier:
                                writer.Write(ident);
                                state = ScannerState.ExpectingTable;
                                break;

                            case KeywordKind.As:
                                writer.Write(ident);
                                state = ScannerState.ExpectingTableAlias;
                                break;

                            default:
                                // implicit alias, context reset, or any other keyword
                                writer.Write(ident);
                                state = ScannerState.Default;
                                break;
                        }

                        break;

                    case ScannerState.ExpectingQualifiedTable: {
                        // The identifier after schema. is the real table name — prefix check + table replacement
                        if (_forbiddenTablePrefix is { } &&
                            ident.Length >= _forbiddenTablePrefix.Length &&
                            Ascii.EqualsIgnoreCase(ident[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                            throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(ident)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                        ApplyTableReplacement(ident, ref writer);
                        state = ScannerState.AfterTable;

                        break;
                    }

                    case ScannerState.ExpectingAlias:
                    case ScannerState.AfterCast:
                        // Alias after AS or type name after :: — copy verbatim, no replacement
                        writer.Write(ident);
                        state = ScannerState.Default;
                        break;

                    case ScannerState.ExpectingTableAlias:
                        // Alias after AS in table context — copy verbatim, return to table context
                        writer.Write(ident);
                        state = ScannerState.AfterTable;
                        break;

                    case ScannerState.ExpectingCTE:
                        if (keyword == KeywordKind.Recursive)
                            // RECURSIVE is transparent — stay in ExpectingCTE
                            writer.Write(ident);
                        else {
                            // CTE name — copy verbatim, no replacement
                            writer.Write(ident);
                            state = ScannerState.AfterCTE;
                        }

                        break;

                    case ScannerState.AfterCTE:
                        writer.Write(ident);
                        state = ScannerState.Default;
                        break;

                    case ScannerState.AfterLambda:
                        // Lambda parameter name — copy verbatim, no replacement
                        writer.Write(ident);
                        // Stay in AfterLambda (multi-parameter lambda)
                        break;
                }

                continue;
            }

            // :: cast operator
            if (ch == (byte)':' && i + 1 < query.Length && query[i + 1] == (byte)':') {
                writer.Add((byte)':');
                writer.Add((byte)':');
                i     += 2;
                state =  ScannerState.AfterCast;
                continue;
            }

            // : in AfterLambda → end of lambda parameters, transition back to Default
            if (ch == (byte)':' && state == ScannerState.AfterLambda) {
                writer.Add((byte)':');
                i++;
                state = ScannerState.Default;
                continue;
            }

            // dot in AfterTable → schema.table; the identifier already written was the schema name
            if (ch == (byte)'.' && state == ScannerState.AfterTable) {
                writer.Add((byte)'.');
                i++;
                state = ScannerState.ExpectingQualifiedTable;
                continue;
            }

            // comma in AfterTable → ExpectingTable (multi-table FROM lists)
            if (ch == (byte)',' && state == ScannerState.AfterTable) {
                writer.Add(ch);
                i++;
                state = ScannerState.ExpectingTable;
                continue;
            }

            // comma in AfterCTE → ExpectingCTE (multiple CTEs)
            if (ch == (byte)',' && state == ScannerState.AfterCTE) {
                writer.Add(ch);
                i++;
                state = ScannerState.ExpectingCTE;
                continue;
            }

            // ( push state onto stack, reset to Default
            if (ch == (byte)'(') {
                if (depth >= 32)
                    throw new QueryNormalizationException("Nesting depth exceeds maximum of 32");

                stateStack[depth] = state;
                depth++;
                state = ScannerState.Default;
                writer.Add(ch);
                i++;
                continue;
            }

            // ) pop state from stack
            if (ch == (byte)')') {
                if (depth > 0) {
                    depth--;
                    state = stateStack[depth];

                    // If the subquery was in table position, the subquery served as the table
                    if (state == ScannerState.ExpectingTable)
                        state = ScannerState.AfterTable;
                }

                writer.Add(ch);
                i++;
                continue;
            }

            // numeric literal with possible underscore separators (e.g. 100_000)
            if (ch >= (byte)'0' && ch <= (byte)'9') {
                var numRest = query[i..];
                var numEnd  = numRest.IndexOfAnyExcept(NumericBytes);
                var numLen  = numEnd < 0 ? numRest.Length : numEnd;
                writer.Write(numRest[..numLen]);
                i += numLen;
                continue;
            }

            // default: copy byte
            writer.Add(ch);
            i++;
        }
    }

    void ApplyColumnReplacement(ReadOnlySpan<byte> ident, ref BufferWriterSlim<byte> writer) {
        if (_columnLookup.TryGetValue(ident, out var replacement))
            writer.Write(replacement!);
        else
            writer.Write(ident);
    }

    void ApplyTableReplacement(ReadOnlySpan<byte> ident, ref BufferWriterSlim<byte> writer) {
        if (_tableLookup.TryGetValue(ident, out var replacement))
            writer.Write(replacement!);
        else
            writer.Write(ident);
    }

    static bool IsAsciiLetter(byte b) =>
        (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z');

    static bool IsIdentifierStart(byte b) => IsAsciiLetter(b) || b == (byte)'_';

    static bool IsAsciiWhitespace(byte b) =>
        b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';

    static KeywordKind ClassifyKeyword(ReadOnlySpan<byte> ident) {
        switch (ident.Length) {
            case 2:
                if (Ascii.EqualsIgnoreCase(ident, "AS"u8)) return KeywordKind.As;
                if (Ascii.EqualsIgnoreCase(ident, "ON"u8)) return KeywordKind.ContextReset;
                return KeywordKind.None;

            case 3:
                if (Ascii.EqualsIgnoreCase(ident, "SET"u8)) return KeywordKind.ContextReset;
                return KeywordKind.None;

            case 4:
                if (Ascii.EqualsIgnoreCase(ident, "FROM"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "JOIN"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "INTO"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "WITH"u8)) return KeywordKind.With;
                if (Ascii.EqualsIgnoreCase(ident, "LEFT"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "FULL"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "SEMI"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "ANTI"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "ASOF"u8)) return KeywordKind.JoinModifier;
                return KeywordKind.None;

            case 5:
                if (Ascii.EqualsIgnoreCase(ident, "WHERE"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "TABLE"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "PIVOT"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "ORDER"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "GROUP"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "LIMIT"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "UNION"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "USING"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "RIGHT"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "INNER"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "OUTER"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "CROSS"u8)) return KeywordKind.JoinModifier;
                return KeywordKind.None;

            case 6:
                if (Ascii.EqualsIgnoreCase(ident, "SELECT"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "UPDATE"u8)) return KeywordKind.TableIntroducer;
                if (Ascii.EqualsIgnoreCase(ident, "HAVING"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "VALUES"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "WINDOW"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "OFFSET"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "EXCEPT"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "FILTER"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "SAMPLE"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "LAMBDA"u8)) return KeywordKind.Lambda;
                return KeywordKind.None;

            case 7:
                if (Ascii.EqualsIgnoreCase(ident, "NATURAL"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "LATERAL"u8)) return KeywordKind.JoinModifier;
                if (Ascii.EqualsIgnoreCase(ident, "QUALIFY"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "UNPIVOT"u8)) return KeywordKind.TableIntroducer;
                return KeywordKind.None;

            case 9:
                if (Ascii.EqualsIgnoreCase(ident, "RETURNING"u8)) return KeywordKind.ContextReset;
                if (Ascii.EqualsIgnoreCase(ident, "RECURSIVE"u8)) return KeywordKind.Recursive;
                if (Ascii.EqualsIgnoreCase(ident, "INTERSECT"u8)) return KeywordKind.ContextReset;
                return KeywordKind.None;

            case 10:
                if (Ascii.EqualsIgnoreCase(ident, "POSITIONAL"u8)) return KeywordKind.JoinModifier;
                return KeywordKind.None;

            default:
                return KeywordKind.None;
        }
    }

    static bool PeekColonAlias(ReadOnlySpan<byte> query, int pos, out int colonPos) {
        var j = pos;

        while (j < query.Length && IsAsciiWhitespace(query[j]))
            j++;

        if (j < query.Length && query[j] == (byte)':') {
            // Rule out :: (cast operator)
            if (j + 1 < query.Length && query[j + 1] == (byte)':') {
                colonPos = -1;
                return false;
            }

            colonPos = j;
            return true;
        }

        colonPos = -1;
        return false;
    }

    void HandleDoubleQuotedIdentifier(ReadOnlySpan<byte> query, ref int pos, ref BufferWriterSlim<byte> writer, ref ScannerState state) {
        var start = pos;
        pos++; // skip opening "

        // Scan to closing ", handling "" escapes — vectorized via IndexOf
        var hasEscapes = false;

        while (pos < query.Length) {
            var dqRest = query[pos..];
            var dqHit  = dqRest.IndexOf((byte)'"');

            if (dqHit < 0) {
                pos = query.Length;
                break;
            }

            pos += dqHit + 1;

            if (pos < query.Length && query[pos] == (byte)'"') {
                hasEscapes = true;
                pos++;
                continue;
            }

            break;
        }

        // Content between the outer quotes (including any "" escapes)
        var rawContent = query[(start + 1)..(pos - 1)];

        // For lookup, unescape "" → " if needed (heap alloc only on escape path, which is rare)
        ReadOnlySpan<byte> lookupContent;
        byte[]? unescapedArray = null;

        if (hasEscapes) {
            unescapedArray = new byte[rawContent.Length];
            var uLen = 0;

            for (var k = 0; k < rawContent.Length; k++) {
                unescapedArray[uLen++] = rawContent[k];

                if (rawContent[k] == (byte)'"' && k + 1 < rawContent.Length && rawContent[k + 1] == (byte)'"')
                    k++; // skip second "
            }

            lookupContent = unescapedArray.AsSpan(0, uLen);
        }
        else
            lookupContent = rawContent;

        switch (state) {
            case ScannerState.Default: {
                if (_columnLookup.TryGetValue(lookupContent, out var replacement)) {
                    writer.Add((byte)'"');
                    writer.Write(replacement!);
                    writer.Add((byte)'"');
                }
                else
                    writer.Write(query[start..pos]);

                break;
            }

            case ScannerState.ExpectingTable:
            case ScannerState.ExpectingQualifiedTable: {
                if (_forbiddenTablePrefix is { } &&
                    lookupContent.Length >= _forbiddenTablePrefix.Length &&
                    Ascii.EqualsIgnoreCase(lookupContent[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                    throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(lookupContent)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                if (_tableLookup.TryGetValue(lookupContent, out var replacement)) {
                    writer.Add((byte)'"');
                    writer.Write(replacement!);
                    writer.Add((byte)'"');
                }
                else
                    writer.Write(query[start..pos]);

                state = ScannerState.AfterTable;
                break;
            }

            case ScannerState.ExpectingAlias:
            case ScannerState.AfterTable:
            case ScannerState.AfterCast:
                // Alias, implicit alias, or type name — copy verbatim
                writer.Write(query[start..pos]);
                state = ScannerState.Default;
                break;

            case ScannerState.ExpectingTableAlias:
                // Double-quoted alias in table context — copy verbatim, return to table context
                writer.Write(query[start..pos]);
                state = ScannerState.AfterTable;
                break;

            default: writer.Write(query[start..pos]); break;
        }

    }

    static void SkipLineComment(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        writer.Add((byte)'-');
        writer.Add((byte)'-');
        pos += 2;
        var rest = q[pos..];
        var nl   = rest.IndexOf((byte)'\n');

        if (nl < 0) {
            writer.Write(rest);
            pos = q.Length;
        }
        else {
            writer.Write(rest[..(nl + 1)]);
            pos += nl + 1;
        }
    }

    static void SkipBlockComment(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        writer.Add((byte)'/');
        writer.Add((byte)'*');
        pos += 2;
        var depth = 1;

        while (pos < q.Length && depth > 0) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny((byte)'*', (byte)'/');

            if (hit < 0) {
                writer.Write(rest);
                pos = q.Length;
                return;
            }

            if (hit > 0) {
                writer.Write(rest[..hit]);
                pos += hit;
            }

            if (pos + 1 < q.Length && q[pos] == (byte)'/' && q[pos + 1] == (byte)'*') {
                writer.Add((byte)'/');
                writer.Add((byte)'*');
                pos += 2;
                depth++;
            }
            else if (pos + 1 < q.Length && q[pos] == (byte)'*' && q[pos + 1] == (byte)'/') {
                writer.Add((byte)'*');
                writer.Add((byte)'/');
                pos += 2;
                depth--;
            }
            else {
                writer.Add(q[pos]);
                pos++;
            }
        }
    }

    static void SkipStringLiteral(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        writer.Add((byte)'\'');
        pos++;

        while (pos < q.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOf((byte)'\'');

            if (hit < 0) {
                writer.Write(rest);
                pos = q.Length;
                return;
            }

            if (hit > 0) {
                writer.Write(rest[..hit]);
                pos += hit;
            }

            writer.Add((byte)'\'');
            pos++;

            // '' is escaped quote, continue
            if (pos < q.Length && q[pos] == (byte)'\'') {
                writer.Add((byte)'\'');
                pos++;
                continue;
            }

            // single quote ends the string
            return;
        }
    }

    static void SkipEscapeString(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        writer.Add(q[pos]); // E or e
        pos++;
        writer.Add((byte)'\'');
        pos++;

        while (pos < q.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny((byte)'\\', (byte)'\'');

            if (hit < 0) {
                writer.Write(rest);
                pos = q.Length;
                return;
            }

            if (hit > 0) {
                writer.Write(rest[..hit]);
                pos += hit;
            }

            if (q[pos] == (byte)'\\' && pos + 1 < q.Length) {
                writer.Add((byte)'\\');
                pos++;
                writer.Add(q[pos]);
                pos++;
                continue;
            }

            if (q[pos] == (byte)'\'') {
                writer.Add((byte)'\'');
                pos++;

                if (pos < q.Length && q[pos] == (byte)'\'') {
                    writer.Add((byte)'\'');
                    pos++;
                    continue;
                }

                return;
            }

            writer.Add(q[pos]);
            pos++;
        }
    }

    static void SkipDollarQuotedString(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        // Find the opening delimiter: $$ or $tag$
        var start = pos;
        pos++; // skip first $

        // scan for tag name (alphanumeric/underscore) until next $
        while (pos < q.Length && q[pos] != (byte)'$') {
            var c = q[pos];

            if (IsAsciiLetter(c) || (c >= (byte)'0' && c <= (byte)'9') || c == (byte)'_')
                pos++;
            else {
                // not a valid dollar-quoted string tag, just copy the initial $
                pos = start;
                writer.Add(q[pos]);
                pos++;
                return;
            }
        }

        if (pos >= q.Length) {
            // no closing $ for tag, just copy the initial $
            pos = start;
            writer.Add(q[pos]);
            pos++;
            return;
        }

        pos++; // skip closing $ of delimiter
        var delimiter = q[start..pos];
        writer.Write(delimiter);

        // find closing delimiter
        while (pos <= q.Length - delimiter.Length) {
            var rest = q[pos..];
            var hit  = rest.IndexOf((byte)'$');

            if (hit < 0) {
                writer.Write(rest);
                pos = q.Length;
                return;
            }

            if (hit > 0) {
                writer.Write(rest[..hit]);
                pos += hit;
            }

            if (pos <= q.Length - delimiter.Length &&
                q.Slice(pos, delimiter.Length).SequenceEqual(delimiter)) {
                writer.Write(delimiter);
                pos += delimiter.Length;
                return;
            }

            writer.Add(q[pos]);
            pos++;
        }

        // no closing delimiter found, copy remaining
        writer.Write(q[pos..]);
        pos = q.Length;
    }

    static void SkipBracket(ReadOnlySpan<byte> q, ref int pos, ref BufferWriterSlim<byte> writer) {
        writer.Add((byte)'[');
        pos++;
        var depth = 1;

        while (pos < q.Length && depth > 0) {
            var rest = q[pos..];
            var hit  = rest.IndexOfAny((byte)'[', (byte)']');

            if (hit < 0) {
                writer.Write(rest);
                pos = q.Length;
                return;
            }

            if (hit > 0) {
                writer.Write(rest[..hit]);
                pos += hit;
            }

            if (q[pos] == (byte)'[')
                depth++;
            else if (q[pos] == (byte)']')
                depth--;

            writer.Add(q[pos]);
            pos++;
        }
    }
}
