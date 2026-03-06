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

public sealed class QueryExpressionNormalizer {
    readonly NormalizerOptions _options;

    public QueryExpressionNormalizer(NormalizerOptions options) {
        _options = options;
    }

    public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer) {
        var w = new ScanWriter(buffer);

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

            // default: copy character
            w.Copy(ch);
            i++;
        }

        return NormalizeResult.Success(w.Result(buffer));
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
