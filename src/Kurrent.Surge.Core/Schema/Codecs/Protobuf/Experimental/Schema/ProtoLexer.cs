using System.Text;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;

enum TokenKind { Identifier, StringLiteral, IntLiteral, FloatLiteral, Symbol, Eof }

/// <summary>One comment block, with the text the descriptor carries.</summary>
/// <param name="Text">
/// The delimiters removed and the line breaks kept, which is the form <c>SourceCodeInfo</c> stores
/// and <c>protoc</c> produces — <c>// note</c> becomes <c>" note\n"</c>.
/// </param>
/// <param name="Detached">
/// A blank line separates this block from the declaration, so it comments the file rather than the
/// declaration below it.
/// </param>
/// <param name="Line">Where the block starts, so a caller can tell a trailing comment from a leading one.</param>
readonly record struct ProtoComment(string Text, bool Detached, int Line);

/// <param name="EndLine">The line the token ends on, which is where a span closes.</param>
/// <param name="EndColumn">The column just past the token's last character.</param>
/// <param name="Leading">The comment blocks between the previous token and this one, or null when there were none.</param>
readonly record struct Token(
    TokenKind Kind, string Value, int Line, int Column,
    int EndLine = 0, int EndColumn = 0, IReadOnlyList<ProtoComment>? Leading = null
);

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

    /// <summary>The token most recently consumed, which is where the current declaration ends.</summary>
    /// <remarks>
    /// Held here rather than returned up the call chain: a declaration's closing token is consumed
    /// several frames below the code that records its span, and threading it back would change every
    /// signature between the two for a value the lexer already has.
    /// </remarks>
    public Token LastToken { get; private set; }

    public Token Next() {
        var token = _peeked ?? Scan();

        _peeked   = null;
        LastToken = token;

        return token;
    }

    /// <summary>
    /// Takes the pending comment that sits on <paramref name="line"/>, which makes it trail the
    /// declaration ending there rather than lead the one that follows.
    /// </summary>
    /// <remarks>
    /// The lexer gathers a comment onto the next token it produces, so a trailing comment arrives
    /// attached to the wrong declaration. Only the caller knows where the previous one ended.
    /// </remarks>
    public string? TakeTrailingComment(int line) {
        if (Peek().Leading is not { Count: > 0 } leading) return null;

        // Detachment is not consulted. It records a blank line after the block, and a comment that
        // starts on the declaration's own last line trails it whatever follows — otherwise a blank line
        // between the two declarations hands the comment to the wrong one.
        if (leading[0].Line != line) return null;

        _peeked = _peeked!.Value with { Leading = leading.Count == 1 ? null : leading.Skip(1).ToList() };

        return leading[0].Text;
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

        // Stamped once here rather than in each reader: every one of them ends with the position
        // already sitting past the token, so the end is the same expression four times over.
        var leading = TakeComments();

        if (_pos >= source.Length) return new(TokenKind.Eof, "", _line, _col, _line, _col, leading);

        var startLine = _line;
        var startCol  = _col;
        var c         = source[_pos];

        Token token;

        if (c is '"' or '\'') {
            token = ReadString(startLine, startCol);
        }
        else if (char.IsDigit(c) || (c == '-' && _pos + 1 < source.Length && char.IsDigit(source[_pos + 1]))) {
            token = ReadNumber(startLine, startCol);
        }
        else if (char.IsLetter(c) || c == '_') {
            token = ReadIdentifier(startLine, startCol);
        }
        else {
            Advance();

            token = new(TokenKind.Symbol, c.ToString(), startLine, startCol);
        }

        return token with { EndLine = _line, EndColumn = _col, Leading = leading };
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

        return new(TokenKind.StringLiteral, text.ToString(), startLine, startCol);
    }

    Token ReadNumber(int startLine, int startCol) {
        var start = _pos;

        if (source[_pos] == '-') Advance();

        if (_pos + 1 < source.Length && source[_pos] == '0' && source[_pos + 1] is 'x' or 'X') {
            Advance();
            Advance();

            while (_pos < source.Length && IsHexDigit(source[_pos])) Advance();

            return new(TokenKind.IntLiteral, source[start.._pos], startLine, startCol);
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

        return new(isFloat ? TokenKind.FloatLiteral : TokenKind.IntLiteral, source[start.._pos], startLine, startCol);
    }

    Token ReadIdentifier(int startLine, int startCol) {
        var start = _pos;

        while (_pos < source.Length && (char.IsLetterOrDigit(source[_pos]) || source[_pos] == '_')) Advance();

        return new(TokenKind.Identifier, source[start.._pos], startLine, startCol);
    }

    // Null until a comment is seen, which is the overwhelmingly common case for a token. Allocating
    // a list per token would charge every identifier in the file for a feature few of them use.
    List<ProtoComment>? _comments;

    // Only a run of // lines merges into one block, so the previous block's kind decides.
    bool _lastCommentWasLine;

    // The run currently being merged. A block's text is built here and materialised once, because
    // rebuilding the whole merged string per line is quadratic — one 264-line block in
    // google/api/http.proto copied 2.7 MB that way.
    readonly StringBuilder _block = new();

    int _blockIndex = -1;

    IReadOnlyList<ProtoComment>? TakeComments() {
        CloseBlock();

        var comments = _comments;

        _comments           = null;
        _lastCommentWasLine = false;

        return comments;
    }

    /// <summary>Materialises the open run into the block it was accumulating.</summary>
    void CloseBlock() {
        if (_blockIndex < 0) return;

        _comments![_blockIndex] = _comments[_blockIndex] with { Text = _block.ToString() };

        _block.Clear();

        _blockIndex = -1;
    }

    /// <remarks>
    /// A run of <c>//</c> lines with no blank line between them is one block, and a blank line before
    /// a block detaches it from what follows. Both rules are <c>protoc</c>'s, because the descriptor
    /// is read by tooling that already expects them.
    /// See https://protobuf.dev/reference/protobuf/google.protobuf/#source-code-info.
    /// </remarks>
    void SkipWhitespaceAndComments() {
        var newlines = 0;

        while (_pos < source.Length) {
            var c = source[_pos];

            if (char.IsWhiteSpace(c)) {
                if (c == '\n') newlines++;

                Advance();
                continue;
            }

            if (c == '/' && _pos + 1 < source.Length && source[_pos + 1] == '/') {
                ReadLineComment(newlines);
                newlines = 0;
                continue;
            }

            if (c == '/' && _pos + 1 < source.Length && source[_pos + 1] == '*') {
                ReadBlockComment(newlines);
                newlines = 0;
                continue;
            }

            break;
        }

        // A blank line between the last block and the declaration detaches it, the same way a blank
        // line between two blocks detaches the earlier one. Both are decided by what comes after the
        // block, so the last one cannot be settled until the run of whitespace ends.
        if (newlines > 1) DetachLast();
    }

    void ReadLineComment(int precedingNewlines) {
        var startLine = _line;

        Advance();
        Advance();

        var start = _pos;

        while (_pos < source.Length && source[_pos] != '\n') Advance();

        // A single newline continues the run; a blank line ends it and starts a new block.
        if (precedingNewlines <= 1 && _lastCommentWasLine && _blockIndex >= 0) {
            _block.Append(source.AsSpan(start.._pos)).Append('\n');
            return;
        }

        CloseBlock();

        Add(new ProtoComment("", Detached: false, startLine), precedingNewlines);

        _blockIndex         = _comments!.Count - 1;
        _lastCommentWasLine = true;

        _block.Append(source.AsSpan(start.._pos)).Append('\n');
    }

    void ReadBlockComment(int precedingNewlines) {
        CloseBlock();

        var startLine = _line;

        Advance();
        Advance();

        var start = _pos;

        while (_pos + 1 < source.Length && !(source[_pos] == '*' && source[_pos + 1] == '/')) Advance();

        var text = source[start.._pos];

        if (_pos + 1 < source.Length) {
            Advance();
            Advance();
        }

        Add(new ProtoComment(Undecorate(text), Detached: false, startLine), precedingNewlines);

        _lastCommentWasLine = false;
    }

    /// <summary>Strips the leading <c>*</c> a block comment's continuation lines conventionally carry.</summary>
    /// <remarks>
    /// What <c>protoc</c> stores, and the reason a block comment and a run of <c>//</c> lines saying the
    /// same thing produce the same text. Without it a javadoc-style block keeps its decoration, which
    /// no reader wants and which cannot survive being written back out as line comments.
    /// </remarks>
    static string Undecorate(string text) {
        if (!text.Contains('*')) return text;

        var lines = text.Split('\n');

        for (var index = 0; index < lines.Length; index++) {
            var line    = lines[index];
            var trimmed = line.AsSpan().TrimStart();

            if (trimmed.Length == 0 || trimmed[0] != '*') continue;

            // The star and one space after it, which is the conventional spelling. Anything further
            // is the author's own indentation and is left alone.
            var rest = trimmed[1..];

            lines[index] = rest.Length > 0 && rest[0] == ' ' ? rest[1..].ToString() : rest.ToString();
        }

        return string.Join('\n', lines);
    }

    void Add(ProtoComment comment, int precedingNewlines) {
        if (precedingNewlines > 1) DetachLast();

        (_comments ??= []).Add(comment);
    }

    void DetachLast() {
        if (_comments is { Count: > 0 } comments)
            comments[^1] = comments[^1] with { Detached = true };
    }

    void Advance() {
        if (_pos >= source.Length) return;

        if (source[_pos] == '\n') {
            _line++;
            _col = 1;
        }
        else
            _col++;

        _pos++;
    }

    static bool IsHexDigit(char c) => char.IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
