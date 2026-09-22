namespace ProtobufTranscoder.Parser;

enum TokenKind
{
    Identifier,
    StringLiteral,
    IntLiteral,
    FloatLiteral,
    Symbol,
    Eof,
}

readonly record struct Token(TokenKind Kind, string Value, int Line, int Column);

/// <summary>
/// Simple lexer for .proto files. Handles identifiers, numbers, strings, and symbols.
/// </summary>
sealed class ProtoLexer
{
    readonly string _source;
    readonly string _fileName;
    int             _pos;
    int             _line = 1;
    int             _col  = 1;

    public ProtoLexer(string fileName, string source)
    {
        _fileName = fileName;
        _source = source;
    }

    public string FileName => _fileName;
    public int Line => _line;
    public int Column => _col;

    public Token Next()
    {
        SkipWhitespaceAndComments();
        if (_pos >= _source.Length)
            return new Token(TokenKind.Eof, "", _line, _col);

        var startLine = _line;
        var startCol = _col;
        var c = _source[_pos];

        // String literals
        if (c is '"' or '\'')
            return ReadString(startLine, startCol);

        // Numbers
        if (char.IsDigit(c) || (c == '-' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return ReadNumber(startLine, startCol);

        // Identifiers / keywords
        if (char.IsLetter(c) || c == '_')
            return ReadIdentifier(startLine, startCol);

        // Dot-prefixed identifiers (fully qualified names)
        if (c == '.' && _pos + 1 < _source.Length && (char.IsLetter(_source[_pos + 1]) || _source[_pos + 1] == '_'))
        {
            // Return just the dot as a symbol
        }

        // Symbols
        Advance();
        return new Token(TokenKind.Symbol, c.ToString(), startLine, startCol);
    }

    public Token Peek()
    {
        var savedPos = _pos;
        var savedLine = _line;
        var savedCol = _col;
        var token = Next();
        _pos = savedPos;
        _line = savedLine;
        _col = savedCol;
        return token;
    }

    Token ReadString(int startLine, int startCol)
    {
        var quote = _source[_pos];
        Advance(); // skip opening quote
        var sb = new System.Text.StringBuilder();

        while (_pos < _source.Length && _source[_pos] != quote)
        {
            if (_source[_pos] == '\\')
            {
                Advance();
                if (_pos >= _source.Length) break;
                var escaped = _source[_pos];
                if (escaped == 'x' && _pos + 2 < _source.Length
                    && IsHexDigit(_source[_pos + 1]) && IsHexDigit(_source[_pos + 2]))
                {
                    // Hex escape: \xNN
                    var hex = _source.Substring(_pos + 1, 2);
                    sb.Append((char)Convert.ToByte(hex, 16));
                    Advance(); // x
                    Advance(); // first hex digit
                    Advance(); // second hex digit
                }
                else if (escaped is >= '0' and <= '7')
                {
                    // Octal escape: \NNN (up to 3 digits)
                    var val = escaped - '0';
                    Advance();
                    for (var j = 0; j < 2 && _pos < _source.Length && _source[_pos] is >= '0' and <= '7'; j++)
                    {
                        val = val * 8 + (_source[_pos] - '0');
                        Advance();
                    }
                    sb.Append((char)val);
                }
                else
                {
                    sb.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        'a' => '\a',
                        'b' => '\b',
                        'f' => '\f',
                        'v' => '\v',
                        _ => escaped,
                    });
                    Advance();
                }
            }
            else
            {
                sb.Append(_source[_pos]);
                Advance();
            }
        }

        if (_pos < _source.Length) Advance(); // skip closing quote

        return new Token(TokenKind.StringLiteral, sb.ToString(), startLine, startCol);
    }

    Token ReadNumber(int startLine, int startCol)
    {
        var start = _pos;
        if (_source[_pos] == '-') Advance();

        // Hex
        if (_pos + 1 < _source.Length && _source[_pos] == '0' && _source[_pos + 1] is 'x' or 'X')
        {
            Advance();
            Advance();
            while (_pos < _source.Length && IsHexDigit(_source[_pos])) Advance();
            return new Token(TokenKind.IntLiteral, _source[start.._pos], startLine, startCol);
        }

        var isFloat = false;
        while (_pos < _source.Length && char.IsDigit(_source[_pos])) Advance();

        if (_pos < _source.Length && _source[_pos] == '.')
        {
            isFloat = true;
            Advance();
            while (_pos < _source.Length && char.IsDigit(_source[_pos])) Advance();
        }

        if (_pos < _source.Length && _source[_pos] is 'e' or 'E')
        {
            isFloat = true;
            Advance();
            if (_pos < _source.Length && _source[_pos] is '+' or '-') Advance();
            while (_pos < _source.Length && char.IsDigit(_source[_pos])) Advance();
        }

        var kind = isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral;
        return new Token(kind, _source[start.._pos], startLine, startCol);
    }

    Token ReadIdentifier(int startLine, int startCol)
    {
        var start = _pos;
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_'))
            Advance();
        return new Token(TokenKind.Identifier, _source[start.._pos], startLine, startCol);
    }

    void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            var c = _source[_pos];
            if (char.IsWhiteSpace(c))
            {
                Advance();
                continue;
            }

            // Line comment
            if (c == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '/')
            {
                while (_pos < _source.Length && _source[_pos] != '\n') Advance();
                continue;
            }

            // Block comment
            if (c == '/' && _pos + 1 < _source.Length && _source[_pos + 1] == '*')
            {
                Advance();
                Advance();
                while (_pos + 1 < _source.Length && !(_source[_pos] == '*' && _source[_pos + 1] == '/'))
                    Advance();
                if (_pos + 1 < _source.Length) { Advance(); Advance(); }
                continue;
            }

            break;
        }
    }

    void Advance()
    {
        if (_pos < _source.Length)
        {
            if (_source[_pos] == '\n')
            {
                _line++;
                _col = 1;
            }
            else
            {
                _col++;
            }
            _pos++;
        }
    }

    static bool IsHexDigit(char c) =>
        char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
