using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Client.Queries;

/// <summary>
/// Parses <c>SELECT proj_list FROM kdb.records [WHERE expr] [LIMIT n]</c> into a <see cref="ParsedQuery"/>.
/// Projection paths are preserved as raw strings. WHERE clauses are delegated to <see cref="ExpressionFilterParser"/>.
/// No wildcards, no transformations, no joins, no subqueries.
/// </summary>
public static class QuerySqlParser {
    public static ParsedQuery Parse(string sql) => Parse(sql.AsSpan());

    public static ParsedQuery Parse(ReadOnlySpan<char> sql) {
        if (sql.IsEmpty || sql.IsWhiteSpace())
            throw new QueryParseException("query is empty", 0);

        var s = new Scanner(sql);

        s.SkipWhitespace();
        s.ExpectKeyword("SELECT");

        var fields = ReadProjectionList(ref s);

        s.ExpectKeyword("FROM");
        ExpectTable(ref s, "kdb", "records");

        IFilterExpression? where = null;
        int?               limit = null;

        if (s.TryMatchKeyword("WHERE")) {
            var whereText = s.ReadUntilTopLevelKeyword("LIMIT");
            if (whereText.IsEmpty || whereText.IsWhiteSpace())
                throw new QueryParseException("WHERE clause is empty", s.Position);

            where = ExpressionFilterParser.Parse(whereText);
        }

        if (s.TryMatchKeyword("LIMIT"))
            limit = s.ReadNonNegativeInt();

        s.SkipWhitespace();
        if (!s.IsAtEnd)
            throw new QueryParseException($"unexpected trailing input at position {s.Position}", s.Position);

        return new ParsedQuery(fields, where, limit);
    }

    static IReadOnlyList<ProjectionField> ReadProjectionList(ref Scanner s) {
        var fields = new List<ProjectionField>();

        while (true) {
            if (s.PeekAnyKeyword("FROM", "WHERE", "LIMIT"))
                throw new QueryParseException($"expected projection expression at position {s.Position}", s.Position);

            var path = s.ReadPath();

            string? alias = null;
            if (s.TryMatchKeyword("AS"))
                alias = s.ReadIdentifier();

            fields.Add(new ProjectionField(path, alias));

            if (!s.TryMatchChar(','))
                break;
        }

        return fields;
    }

    static void ExpectTable(ref Scanner s, string qualifier, string table) {
        s.SkipWhitespace();
        var ident1 = s.ReadIdentifier();

        if (!ident1.Equals(qualifier, StringComparison.OrdinalIgnoreCase))
            throw new QueryParseException($"FROM must reference `{qualifier}.{table}` (got `{ident1}`)", s.Position);

        if (!s.TryMatchChar('.'))
            throw new QueryParseException($"FROM must reference `{qualifier}.{table}` (missing `.`)", s.Position);

        var ident2 = s.ReadIdentifier();
        if (!ident2.Equals(table, StringComparison.OrdinalIgnoreCase))
            throw new QueryParseException($"FROM must reference `{qualifier}.{table}` (got `{ident1}.{ident2}`)", s.Position);
    }
}

ref struct Scanner(ReadOnlySpan<char> src) {
    readonly ReadOnlySpan<char> _src = src;
    int _pos;

    public int  Position => _pos;
    public bool IsAtEnd  => _pos >= _src.Length;

    public void SkipWhitespace() {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
            _pos++;
    }

    public bool TryMatchChar(char c) {
        SkipWhitespace();
        if (IsAtEnd || _src[_pos] != c) return false;
        _pos++;
        return true;
    }

    public bool TryMatchKeyword(string keyword) {
        SkipWhitespace();
        if (_pos + keyword.Length > _src.Length) return false;

        for (var i = 0; i < keyword.Length; i++) {
            if (char.ToUpperInvariant(_src[_pos + i]) != char.ToUpperInvariant(keyword[i]))
                return false;
        }

        // require a word boundary after
        var after = _pos + keyword.Length;
        if (after < _src.Length && IsIdentChar(_src[after]))
            return false;

        _pos = after;
        return true;
    }

    public void ExpectKeyword(string keyword) {
        if (!TryMatchKeyword(keyword))
            throw new QueryParseException($"expected `{keyword}` at position {_pos}", _pos);
    }

    public string ReadIdentifier() {
        SkipWhitespace();
        var start = _pos;
        if (IsAtEnd || !IsIdentStartChar(_src[_pos]))
            throw new QueryParseException($"expected identifier at position {_pos}", _pos);

        while (_pos < _src.Length && IsIdentChar(_src[_pos]))
            _pos++;

        return _src[start.._pos].ToString();
    }

    public int ReadNonNegativeInt() {
        SkipWhitespace();
        var start = _pos;
        while (_pos < _src.Length && _src[_pos] is >= '0' and <= '9')
            _pos++;

        if (_pos == start)
            throw new QueryParseException($"expected integer at position {_pos}", _pos);

        if (!int.TryParse(_src[start.._pos], out var value))
            throw new QueryParseException($"invalid integer at position {start}", start);

        return value;
    }

    /// <summary>
    /// Reads a path expression — an identifier followed by any mix of `.ident`, `[...]` blocks,
    /// and `->>` JSON text extraction. Preserved as the raw substring; the PropertyMap path
    /// parser resolves it at execution time.
    /// </summary>
    public string ReadPath() {
        SkipWhitespace();
        var start = _pos;

        if (IsAtEnd || !IsIdentStartChar(_src[_pos]))
            throw new QueryParseException($"expected projection expression at position {_pos}", _pos);

        while (_pos < _src.Length) {
            var c = _src[_pos];

            if (c == '[') {
                ConsumeBracketBlock();
            }
            else if (c == '-' && _pos + 2 < _src.Length && _src[_pos + 1] == '>' && _src[_pos + 2] == '>') {
                _pos += 3;
                SkipOptionalQuotedLiteral();
            }
            else if (IsIdentChar(c) || c == '.') {
                _pos++;
            }
            else {
                break;
            }
        }

        if (_pos == start)
            throw new QueryParseException($"expected projection expression at position {_pos}", _pos);

        return _src[start.._pos].ToString();
    }

    /// <summary>
    /// Reads forward from the current position until a top-level occurrence of
    /// <paramref name="keyword"/> or end-of-input. Tracks quote/paren/bracket depth so that
    /// keywords appearing inside string literals, parentheses, or brackets are ignored.
    /// Returns the span preceding the keyword; positions the scanner BEFORE the keyword so
    /// the caller can consume it with <see cref="TryMatchKeyword"/>.
    /// </summary>
    public ReadOnlySpan<char> ReadUntilTopLevelKeyword(string keyword) {
        SkipWhitespace();
        var start = _pos;

        var parenDepth   = 0;
        var bracketDepth = 0;

        while (_pos < _src.Length) {
            var c = _src[_pos];

            if (c is '\'' or '"') {
                SkipStringLiteral();
                continue;
            }

            if (c == '(') { parenDepth++;   _pos++; continue; }
            if (c == ')') { parenDepth--;   _pos++; continue; }
            if (c == '[') { bracketDepth++; _pos++; continue; }
            if (c == ']') { bracketDepth--; _pos++; continue; }

            if (parenDepth == 0 && bracketDepth == 0 && IsKeywordHere(keyword))
                return _src[start.._pos];

            _pos++;
        }

        return _src[start.._pos];
    }

    /// <summary>
    /// Peeks whether any of the given keywords starts at the current position (after whitespace),
    /// without consuming. Used to guard places where a projection path is expected but a reserved
    /// keyword appears instead.
    /// </summary>
    public bool PeekAnyKeyword(params string[] keywords) {
        var savedPos = _pos;
        SkipWhitespace();

        foreach (var kw in keywords) {
            if (IsKeywordHere(kw)) {
                _pos = savedPos;
                return true;
            }
        }

        _pos = savedPos;
        return false;
    }

    bool IsKeywordHere(string keyword) {
        if (_pos + keyword.Length > _src.Length) return false;
        if (_pos > 0 && IsIdentChar(_src[_pos - 1])) return false;

        for (var i = 0; i < keyword.Length; i++) {
            if (char.ToUpperInvariant(_src[_pos + i]) != char.ToUpperInvariant(keyword[i]))
                return false;
        }

        var after = _pos + keyword.Length;
        return after >= _src.Length || !IsIdentChar(_src[after]);
    }

    void ConsumeBracketBlock() {
        // assumes _src[_pos] == '['
        _pos++;
        var depth = 1;

        while (_pos < _src.Length && depth > 0) {
            var c = _src[_pos];

            if (c is '\'' or '"') { SkipStringLiteral(); continue; }
            if (c == '[') depth++;
            else if (c == ']') depth--;

            _pos++;
        }

        if (depth != 0)
            throw new QueryParseException($"unterminated `[` in path", _pos);
    }

    void SkipOptionalQuotedLiteral() {
        SkipWhitespace();
        if (IsAtEnd) return;
        if (_src[_pos] is '\'' or '"')
            SkipStringLiteral();
    }

    void SkipStringLiteral() {
        var quote = _src[_pos];
        _pos++;

        while (_pos < _src.Length) {
            var c = _src[_pos];
            if (c == '\\' && _pos + 1 < _src.Length) {
                _pos += 2;
                continue;
            }
            if (c == quote) {
                _pos++;
                return;
            }
            _pos++;
        }

        throw new QueryParseException("unterminated string literal", _pos);
    }

    static bool IsIdentStartChar(char c) => char.IsLetter(c) || c == '_';
    static bool IsIdentChar(char c)      => char.IsLetterOrDigit(c) || c == '_';
}
