using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Kurrent.Client.Streams;

public sealed record NormalizerOptions {
    public ImmutableArray<KeyValuePair<string, string>> ColumnReplacements   { get; init; } = [];
    public ImmutableArray<KeyValuePair<string, string>> TableReplacements    { get; init; } = [];
    public string?                                      ForbiddenTablePrefix { get; init; }
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

ref struct ScanWriter {
    readonly Span<char> _buffer;
    int                 _written;

    public ScanWriter(Span<char> buffer) {
        _buffer  = buffer;
        _written = 0;
    }

    public int Written => _written;

    public void Copy(char c) => _buffer[_written++] = c;

    public void CopySlice(ReadOnlySpan<char> slice) {
        slice.CopyTo(_buffer[_written..]);
        _written += slice.Length;
    }

    public ReadOnlySpan<char> Result(Span<char> buffer) => buffer[.._written];
}

enum ScannerState {
    Default,                // Column context
    ExpectingTable,         // After FROM/JOIN/INTO/UPDATE/TABLE
    AfterTable,             // After consuming a table name
    ExpectingQualifiedTable, // After dot in table position (Task 8)
    ExpectingAlias,         // After AS (Task 6)
    ExpectingCTE,           // After WITH (Task 9)
    AfterCTE,               // After CTE name (Task 9)
    AfterLambda,            // After lambda keyword (Task 9)
    AfterCast,              // After :: operator (Task 7)
}

enum KeywordKind {
    None,            // Not a keyword — treat as identifier
    TableIntroducer, // FROM, JOIN, INTO, UPDATE, TABLE, PIVOT, UNPIVOT
    JoinModifier,    // LEFT, RIGHT, INNER, OUTER, FULL, CROSS, NATURAL, LATERAL, SEMI, ANTI, ASOF, POSITIONAL
    ContextReset,    // SELECT, WHERE, ON, SET, ORDER, GROUP, HAVING, LIMIT, UNION, VALUES, RETURNING, QUALIFY, WINDOW, USING, OFFSET, EXCEPT, INTERSECT, FILTER, SAMPLE
    As,              // AS (special — alias)
    With,            // WITH (special — CTE)
    Lambda,          // LAMBDA (special)
    Recursive,       // RECURSIVE (transparent in CTE)
}

public sealed class QueryExpressionNormalizer {
    readonly NormalizerOptions _options;
    readonly string?          _forbiddenTablePrefix;

    readonly FrozenDictionary<string, string> _columnMap;
    readonly FrozenDictionary<string, string> _tableMap;
#if NET9_0_OR_GREATER
    readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _columnLookup;
    readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _tableLookup;
#endif

    public QueryExpressionNormalizer(NormalizerOptions options) {
        _options              = options;
        _forbiddenTablePrefix = options.ForbiddenTablePrefix;

        _columnMap = options.ColumnReplacements.ToFrozenDictionary(
            kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        _tableMap = options.TableReplacements.ToFrozenDictionary(
            kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
#if NET9_0_OR_GREATER
        _columnLookup = _columnMap.GetAlternateLookup<ReadOnlySpan<char>>();
        _tableLookup  = _tableMap.GetAlternateLookup<ReadOnlySpan<char>>();
#endif
    }

    static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    static bool IsIdentifierPart(char c)  => char.IsLetter(c) || char.IsAsciiDigit(c) || c == '_';

    static KeywordKind ClassifyKeyword(ReadOnlySpan<char> ident) {
        switch (ident.Length) {
            case 2:
                if (MemoryExtensions.Equals(ident, "AS", StringComparison.OrdinalIgnoreCase)) return KeywordKind.As;
                if (MemoryExtensions.Equals(ident, "ON", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                break;
            case 3:
                if (MemoryExtensions.Equals(ident, "SET", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                break;
            case 4:
                if (MemoryExtensions.Equals(ident, "FROM", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "JOIN", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "INTO", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "LEFT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "FULL", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "SEMI", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "ANTI", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "ASOF", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "WITH", StringComparison.OrdinalIgnoreCase)) return KeywordKind.With;
                break;
            case 5:
                if (MemoryExtensions.Equals(ident, "TABLE", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "PIVOT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "RIGHT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "INNER", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "OUTER", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "CROSS", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "WHERE", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "ORDER", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "GROUP", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "LIMIT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "UNION", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "USING", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                break;
            case 6:
                if (MemoryExtensions.Equals(ident, "UPDATE", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "SELECT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "HAVING", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "VALUES", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "WINDOW", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "OFFSET", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "EXCEPT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "FILTER", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "SAMPLE", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "LAMBDA", StringComparison.OrdinalIgnoreCase)) return KeywordKind.Lambda;
                break;
            case 7:
                if (MemoryExtensions.Equals(ident, "UNPIVOT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.TableIntroducer;
                if (MemoryExtensions.Equals(ident, "NATURAL", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "LATERAL", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                if (MemoryExtensions.Equals(ident, "QUALIFY", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                break;
            case 9:
                if (MemoryExtensions.Equals(ident, "RETURNING", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "INTERSECT", StringComparison.OrdinalIgnoreCase)) return KeywordKind.ContextReset;
                if (MemoryExtensions.Equals(ident, "RECURSIVE", StringComparison.OrdinalIgnoreCase)) return KeywordKind.Recursive;
                break;
            case 10:
                if (MemoryExtensions.Equals(ident, "POSITIONAL", StringComparison.OrdinalIgnoreCase)) return KeywordKind.JoinModifier;
                break;
        }
        return KeywordKind.None;
    }

    public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer) {
        var w     = new ScanWriter(buffer);
        var state = ScannerState.Default;

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
            if ((ch == 'E' || ch == 'e') && i + 1 < query.Length && query[i + 1] == '\'') {
                SkipEscapeString(query, ref i, ref w);
                continue;
            }

            // '...' string literal
            if (ch == '\'') {
                SkipStringLiteral(query, ref i, ref w);
                continue;
            }

            // $$...$$ or $tag$...$tag$ dollar-quoted string
            if (ch == '$') {
                // If $ is followed by a digit, it's a parameter reference — just copy
                if (i + 1 < query.Length && char.IsDigit(query[i + 1])) {
                    w.Copy(ch);
                    i++;
                    continue;
                }
                // Check for $$ or $tag$
                if (i + 1 < query.Length && (query[i + 1] == '$' || char.IsLetter(query[i + 1]) || query[i + 1] == '_')) {
                    SkipDollarQuotedString(query, ref i, ref w);
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
                var error = HandleDoubleQuotedIdentifier(query, ref i, ref w, ref state);
                if (error is not null)
                    return NormalizeResult.Failure(error);
                continue;
            }

            // bare identifier: scan, classify, and apply state-dependent replacement
            if (IsIdentifierStart(ch)) {
                var start = i;
                i++;
                while (i < query.Length && IsIdentifierPart(query[i]))
                    i++;

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
                            case KeywordKind.None:
                                ApplyColumnReplacement(ident, ref w);
                                break;
                            default:
                                w.CopySlice(ident);
                                break;
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
                                if (_forbiddenTablePrefix is not null &&
                                    ident.StartsWith(_forbiddenTablePrefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                                    return NormalizeResult.Failure($"Table '{ident.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'");
                                }
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
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingTable;
                                break;
                            case KeywordKind.As:
                                w.CopySlice(ident);
                                state = ScannerState.ExpectingAlias;
                                break;
                            case KeywordKind.ContextReset:
                                w.CopySlice(ident);
                                state = ScannerState.Default;
                                break;
                            case KeywordKind.None:
                                // implicit alias — copy verbatim
                                w.CopySlice(ident);
                                state = ScannerState.Default;
                                break;
                            default:
                                w.CopySlice(ident);
                                state = ScannerState.Default;
                                break;
                        }
                        break;

                    case ScannerState.ExpectingQualifiedTable: {
                        // The identifier after schema. is the real table name — prefix check + table replacement
                        if (_forbiddenTablePrefix is not null &&
                            ident.StartsWith(_forbiddenTablePrefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                            return NormalizeResult.Failure($"Table '{ident.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'");
                        }
                        ApplyTableReplacement(ident, ref w);
                        state = ScannerState.AfterTable;
                        break;
                    }

                    case ScannerState.ExpectingAlias:
                        // The identifier after AS is an alias — copy verbatim, no replacement
                        w.CopySlice(ident);
                        state = ScannerState.Default;
                        break;

                    case ScannerState.AfterCast:
                        // The identifier after :: is a type name — copy verbatim, no replacement
                        w.CopySlice(ident);
                        state = ScannerState.Default;
                        break;
                }

                continue;
            }

            // :: cast operator
            if (ch == ':' && i + 1 < query.Length && query[i + 1] == ':') {
                w.Copy(':');
                w.Copy(':');
                i += 2;
                state = ScannerState.AfterCast;
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

            // default: copy character
            w.Copy(ch);
            i++;
        }

        return NormalizeResult.Success(w.Result(buffer));
    }

    void ApplyColumnReplacement(ReadOnlySpan<char> ident, ref ScanWriter w) {
#if NET9_0_OR_GREATER
        if (_columnLookup.TryGetValue(ident, out var replacement))
#else
        if (_columnMap.TryGetValue(ident.ToString(), out var replacement))
#endif
            w.CopySlice(replacement.AsSpan());
        else
            w.CopySlice(ident);
    }

    /// <summary>
    /// Peeks ahead from <paramref name="pos"/> (just past an identifier) to see if
    /// the next non-whitespace character is ':' NOT followed by another ':'.
    /// Returns true and outputs the position of the ':' if found.
    /// </summary>
    static bool PeekColonAlias(ReadOnlySpan<char> query, int pos, out int colonPos) {
        var j = pos;
        while (j < query.Length && query[j] == ' ')
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
    string? HandleDoubleQuotedIdentifier(
        ReadOnlySpan<char> query, ref int pos, ref ScanWriter w, ref ScannerState state
    ) {
        var start = pos;
        pos++; // skip opening "

        // Scan to closing ", handling "" escapes
        var hasEscapes = false;
        while (pos < query.Length) {
            if (query[pos] == '"') {
                pos++;
                if (pos < query.Length && query[pos] == '"') {
                    // "" escape — continue scanning
                    hasEscapes = true;
                    pos++;
                    continue;
                }
                // closing "
                break;
            }
            pos++;
        }

        // Content between the outer quotes (including any "" escapes)
        var rawContent = query[(start + 1)..(pos - 1)];

        // For lookup, unescape "" → " if needed
        ReadOnlySpan<char> lookupContent;
        string? unescaped = null;
        if (hasEscapes) {
            unescaped = rawContent.ToString().Replace("\"\"", "\"");
            lookupContent = unescaped.AsSpan();
        } else {
            lookupContent = rawContent;
        }

        switch (state) {
            case ScannerState.Default: {
                // Column replacement
#if NET9_0_OR_GREATER
                if (_columnLookup.TryGetValue(lookupContent, out var replacement)) {
#else
                var key = unescaped ?? lookupContent.ToString();
                if (_columnMap.TryGetValue(key, out var replacement)) {
#endif
                    w.Copy('"');
                    w.CopySlice(replacement.AsSpan());
                    w.Copy('"');
                } else {
                    // No match — copy original verbatim
                    w.CopySlice(query[start..pos]);
                }
                break;
            }

            case ScannerState.ExpectingTable:
            case ScannerState.ExpectingQualifiedTable: {
                // Prefix check on unescaped content
                if (_forbiddenTablePrefix is not null &&
                    lookupContent.StartsWith(_forbiddenTablePrefix.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    return $"Table '{lookupContent.ToString()}' uses forbidden prefix '{_forbiddenTablePrefix}'";
                }

                // Table replacement
#if NET9_0_OR_GREATER
                if (_tableLookup.TryGetValue(lookupContent, out var replacement)) {
#else
                var key = unescaped ?? lookupContent.ToString();
                if (_tableMap.TryGetValue(key, out var replacement)) {
#endif
                    w.Copy('"');
                    w.CopySlice(replacement.AsSpan());
                    w.Copy('"');
                } else {
                    // No match — copy original verbatim
                    w.CopySlice(query[start..pos]);
                }
                state = ScannerState.AfterTable;
                break;
            }

            case ScannerState.ExpectingAlias:
                // Alias — copy verbatim
                w.CopySlice(query[start..pos]);
                state = ScannerState.Default;
                break;

            case ScannerState.AfterTable:
                // Implicit alias — copy verbatim
                w.CopySlice(query[start..pos]);
                state = ScannerState.Default;
                break;

            case ScannerState.AfterCast:
                // Type name — copy verbatim
                w.CopySlice(query[start..pos]);
                state = ScannerState.Default;
                break;

            default:
                // Copy verbatim for any other state
                w.CopySlice(query[start..pos]);
                break;
        }

        return null;
    }

    void ApplyTableReplacement(ReadOnlySpan<char> ident, ref ScanWriter w) {
#if NET9_0_OR_GREATER
        if (_tableLookup.TryGetValue(ident, out var replacement))
#else
        if (_tableMap.TryGetValue(ident.ToString(), out var replacement))
#endif
            w.CopySlice(replacement.AsSpan());
        else
            w.CopySlice(ident);
    }

    // -- single-line comment: copy until \n or end
    static void SkipLineComment(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('-');
        w.Copy('-');
        pos += 2;
        var rest = q[pos..];
        var nl   = rest.IndexOf('\n');
        if (nl < 0) {
            w.CopySlice(rest);
            pos = q.Length;
        } else {
            w.CopySlice(rest[..(nl + 1)]);
            pos += nl + 1;
        }
    }

    // /* block comment */ with nesting
    static void SkipBlockComment(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('/');
        w.Copy('*');
        pos += 2;
        var depth = 1;
        while (pos < q.Length && depth > 0) {
            if (pos + 1 < q.Length && q[pos] == '/' && q[pos + 1] == '*') {
                w.Copy('/');
                w.Copy('*');
                pos += 2;
                depth++;
            } else if (pos + 1 < q.Length && q[pos] == '*' && q[pos + 1] == '/') {
                w.Copy('*');
                w.Copy('/');
                pos += 2;
                depth--;
            } else {
                w.Copy(q[pos]);
                pos++;
            }
        }
    }

    // 'string literal' with '' escape
    static void SkipStringLiteral(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('\'');
        pos++;
        while (pos < q.Length) {
            if (q[pos] == '\'') {
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
            w.Copy(q[pos]);
            pos++;
        }
    }

    // E'escape string' with \' and '' escapes
    static void SkipEscapeString(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy(q[pos]); // E or e
        pos++;
        w.Copy('\'');
        pos++;
        while (pos < q.Length) {
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

    // $$...$$ or $tag$...$tag$
    static void SkipDollarQuotedString(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        // Find the opening delimiter: $$ or $tag$
        var start = pos;
        pos++; // skip first $
        // scan for tag name (alphanumeric/underscore) until next $
        while (pos < q.Length && q[pos] != '$') {
            var c = q[pos];
            if (char.IsLetterOrDigit(c) || c == '_') {
                pos++;
            } else {
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
            if (q.Slice(pos, delimiter.Length).SequenceEqual(delimiter)) {
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

    // [...] with nested brackets
    static void SkipBracket(ReadOnlySpan<char> q, ref int pos, ref ScanWriter w) {
        w.Copy('[');
        pos++;
        var depth = 1;
        while (pos < q.Length && depth > 0) {
            if (q[pos] == '[') {
                depth++;
            } else if (q[pos] == ']') {
                depth--;
            }
            w.Copy(q[pos]);
            pos++;
        }
    }
}
