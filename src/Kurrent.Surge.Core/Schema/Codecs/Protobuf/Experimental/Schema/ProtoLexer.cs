using System.Text;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

enum TokenKind { Identifier, StringLiteral, IntLiteral, FloatLiteral, Symbol, Eof }

readonly record struct Token(TokenKind Kind, string Value, int Line, int Column);

/// <summary>
/// Tokenizer for <c>.proto</c> text.
/// </summary>
sealed class ProtoLexer(string fileName, string source) {
    int _pos;
    int _line = 1;
    int _col  = 1;

    // The token a Peek has already produced, waiting for the Next that consumes it.
    Token? _peeked;

    public string FileName => fileName;

    public Token Next() {
        if (_peeked is not { } peeked) return Scan();

        _peeked = null;

        return peeked;
    }

    /// <remarks>
    /// The grammar needs one token of lookahead in several places — a field option block, a dotted
    /// type name, a trailing <c>to max</c>. The token is held rather than re-scanned: rewinding the
    /// position and lexing again would tokenize every looked-at token twice, and several of these
    /// sites are loop conditions.
    /// </remarks>
    public Token Peek() => _peeked ??= Scan();

    Token Scan() {
        SkipWhitespaceAndComments();

        if (_pos >= source.Length) return new Token(TokenKind.Eof, "", _line, _col);

        var startLine = _line;
        var startCol  = _col;
        var c         = source[_pos];

        if (c is '"' or '\'') return ReadString(startLine, startCol);

        if (char.IsDigit(c) || (c == '-' && _pos + 1 < source.Length && char.IsDigit(source[_pos + 1])))
            return ReadNumber(startLine, startCol);

        if (char.IsLetter(c) || c == '_') return ReadIdentifier(startLine, startCol);

        Advance();

        return new Token(TokenKind.Symbol, c.ToString(), startLine, startCol);
    }


    // Escapes follow the protobuf language spec, which allows \xNN hex and \NNN octal alongside
    // the usual C escapes: https://protobuf.dev/reference/protobuf/proto3-spec/#string_literals
    Token ReadString(int startLine, int startCol) {
        var quote = source[_pos];

        Advance();

        var text = new StringBuilder();

        while (_pos < source.Length && source[_pos] != quote) {
            if (source[_pos] != '\\') {
                text.Append(source[_pos]);
                Advance();
                continue;
            }

            Advance();

            if (_pos >= source.Length) break;

            var escaped = source[_pos];

            if (escaped == 'x' && _pos + 2 < source.Length && IsHexDigit(source[_pos + 1]) && IsHexDigit(source[_pos + 2])) {
                text.Append((char)Convert.ToByte(source.Substring(_pos + 1, 2), 16));
                Advance();
                Advance();
                Advance();
            }
            else if (escaped is >= '0' and <= '7') {
                var value = escaped - '0';

                Advance();

                for (var i = 0; i < 2 && _pos < source.Length && source[_pos] is >= '0' and <= '7'; i++) {
                    value = value * 8 + (source[_pos] - '0');
                    Advance();
                }

                text.Append((char)value);
            }
            else {
                text.Append(escaped switch {
                    'n'  => '\n',
                    'r'  => '\r',
                    't'  => '\t',
                    '\\' => '\\',
                    '\'' => '\'',
                    '"'  => '"',
                    'a'  => '\a',
                    'b'  => '\b',
                    'f'  => '\f',
                    'v'  => '\v',
                    _    => escaped
                });

                Advance();
            }
        }

        if (_pos < source.Length) Advance();

        return new Token(TokenKind.StringLiteral, text.ToString(), startLine, startCol);
    }

    Token ReadNumber(int startLine, int startCol) {
        var start = _pos;

        if (source[_pos] == '-') Advance();

        if (_pos + 1 < source.Length && source[_pos] == '0' && source[_pos + 1] is 'x' or 'X') {
            Advance();
            Advance();

            while (_pos < source.Length && IsHexDigit(source[_pos])) Advance();

            return new Token(TokenKind.IntLiteral, source[start.._pos], startLine, startCol);
        }

        var isFloat = false;

        while (_pos < source.Length && char.IsDigit(source[_pos])) Advance();

        if (_pos < source.Length && source[_pos] == '.') {
            isFloat = true;

            Advance();

            while (_pos < source.Length && char.IsDigit(source[_pos])) Advance();
        }

        if (_pos < source.Length && source[_pos] is 'e' or 'E') {
            isFloat = true;

            Advance();

            if (_pos < source.Length && source[_pos] is '+' or '-') Advance();

            while (_pos < source.Length && char.IsDigit(source[_pos])) Advance();
        }

        return new Token(isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral, source[start.._pos], startLine, startCol);
    }

    Token ReadIdentifier(int startLine, int startCol) {
        var start = _pos;

        while (_pos < source.Length && (char.IsLetterOrDigit(source[_pos]) || source[_pos] == '_')) Advance();

        return new Token(TokenKind.Identifier, source[start.._pos], startLine, startCol);
    }

    void SkipWhitespaceAndComments() {
        while (_pos < source.Length) {
            var c = source[_pos];

            if (char.IsWhiteSpace(c)) {
                Advance();
                continue;
            }

            if (c == '/' && _pos + 1 < source.Length && source[_pos + 1] == '/') {
                while (_pos < source.Length && source[_pos] != '\n') Advance();
                continue;
            }

            if (c == '/' && _pos + 1 < source.Length && source[_pos + 1] == '*') {
                Advance();
                Advance();

                while (_pos + 1 < source.Length && !(source[_pos] == '*' && source[_pos + 1] == '/')) Advance();

                if (_pos + 1 < source.Length) {
                    Advance();
                    Advance();
                }

                continue;
            }

            break;
        }
    }

    void Advance() {
        if (_pos >= source.Length) return;

        if (source[_pos] == '\n') {
            _line++;
            _col = 1;
        }
        else {
            _col++;
        }

        _pos++;
    }

    static bool IsHexDigit(char c) => char.IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
