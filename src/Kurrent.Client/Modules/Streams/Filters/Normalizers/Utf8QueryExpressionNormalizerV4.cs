using System.Buffers;
using System.Text;

namespace Kurrent.Client.Streams;

/// <summary>
/// V4: V3 engine (single GetSpan, direct indexed writes, single Advance) targeting
/// <see cref="IBufferWriter{T}"/> — no DotNext dependency.
/// </summary>
public sealed class Utf8QueryExpressionNormalizerV4 : IUtf8QueryExpressionNormalizerV2 {
    static readonly SearchValues<byte> IdentifierPartBytes =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_"u8);

    static readonly SearchValues<byte> NumericBytes = SearchValues.Create("0123456789_"u8);

    readonly Utf8ReplacementLookup _columnLookup;
    readonly Utf8ReplacementLookup _tableLookup;
    readonly byte[]? _forbiddenTablePrefix;
    readonly int _maxGrowthFactor;

    public Utf8QueryExpressionNormalizerV4(NormalizerOptions options) {
        _forbiddenTablePrefix = options.ForbiddenTablePrefix is { } prefix
            ? Encoding.UTF8.GetBytes(prefix)
            : null;

        _columnLookup = new(options.ColumnReplacements);
        _tableLookup  = new(options.TableReplacements);

        // Worst-case growth: every input byte is a 1-byte key replaced by the longest value.
        // Precompute max(ceil(valueLen / keyLen)) across all entries. Minimum factor is 1 (no shrinkage).
        var maxFactor = 1;

        foreach (var (key, value) in options.ColumnReplacements) {
            var keyLen = Encoding.UTF8.GetByteCount(key);
            var valLen = Encoding.UTF8.GetByteCount(value) + 2; // +2 for quotes in double-quoted path
            var factor = (valLen + keyLen - 1) / keyLen;        // integer ceil
            if (factor > maxFactor) maxFactor = factor;
        }

        foreach (var (key, value) in options.TableReplacements) {
            var keyLen = Encoding.UTF8.GetByteCount(key);
            var valLen = Encoding.UTF8.GetByteCount(value) + 2;
            var factor = (valLen + keyLen - 1) / keyLen;
            if (factor > maxFactor) maxFactor = factor;
        }

        _maxGrowthFactor = maxFactor;
    }

    public void Normalize(ReadOnlySpan<byte> query, IBufferWriter<byte> writer) {
        var dest = writer.GetSpan(MaxOutputLen(query.Length));
        var pos  = 0;
        NormalizeCore(query, dest, ref pos);
        writer.Advance(pos);
    }

    int MaxOutputLen(int queryLen) => queryLen * _maxGrowthFactor;

    void NormalizeCore(ReadOnlySpan<byte> query, Span<byte> dest, ref int pos) {
        var state = ScannerState.Default;

        Span<ScannerState> stateStack = stackalloc ScannerState[32];

        var depth = 0;

        for (var i = 0; i < query.Length;) {
            var ch = query[i];

            if (ch == (byte)'-' && i + 1 < query.Length && query[i + 1] == (byte)'-') {
                SkipLineComment(query, ref i, dest, ref pos);
                continue;
            }

            if (ch == (byte)'/' && i + 1 < query.Length && query[i + 1] == (byte)'*') {
                SkipBlockComment(query, ref i, dest, ref pos);
                continue;
            }

            if (ch is (byte)'E' or (byte)'e' && i + 1 < query.Length && query[i + 1] == (byte)'\'') {
                SkipEscapeString(query, ref i, dest, ref pos);
                continue;
            }

            if (ch == (byte)'\'') {
                SkipStringLiteral(query, ref i, dest, ref pos);
                continue;
            }

            if (ch == (byte)'$') {
                if (i + 1 < query.Length && query[i + 1] >= (byte)'0' && query[i + 1] <= (byte)'9') {
                    dest[pos++] = ch;
                    i++;

                    while (i < query.Length && query[i] >= (byte)'0' && query[i] <= (byte)'9') {
                        dest[pos++] = query[i];
                        i++;
                    }

                    continue;
                }

                if (i + 1 < query.Length && query[i + 1] == (byte)'$') {
                    SkipDollarQuotedString(query, ref i, dest, ref pos);
                    continue;
                }

                if (i + 1 < query.Length && (IsAsciiLetter(query[i + 1]) || query[i + 1] == (byte)'_')) {
                    var tagRest = query[(i + 1)..];
                    var tagEnd  = tagRest.IndexOfAnyExcept(IdentifierPartBytes);
                    var j       = i + 1 + (tagEnd < 0 ? tagRest.Length : tagEnd);

                    if (j < query.Length && query[j] == (byte)'$') {
                        SkipDollarQuotedString(query, ref i, dest, ref pos);
                        continue;
                    }

                    dest[pos++] = ch;
                    i++;
                    var paramRest = query[i..];
                    var paramEnd  = paramRest.IndexOfAnyExcept(IdentifierPartBytes);
                    var paramLen  = paramEnd < 0 ? paramRest.Length : paramEnd;
                    paramRest[..paramLen].CopyTo(dest[pos..]);
                    pos += paramLen;
                    i   += paramLen;

                    continue;
                }

                dest[pos++] = ch;
                i++;
                continue;
            }

            if (ch == (byte)'[') {
                SkipBracket(query, ref i, dest, ref pos);
                continue;
            }

            if (ch == (byte)'"') {
                HandleDoubleQuotedIdentifier(query, ref i, dest, ref pos, ref state);
                continue;
            }

            if (IsIdentifierStart(ch)) {
                var start     = i;
                var identRest = query[(i + 1)..];
                var identEnd  = identRest.IndexOfAnyExcept(IdentifierPartBytes);

                i += 1 + (identEnd < 0 ? identRest.Length : identEnd);

                var ident   = query[start..i];
                var keyword = ClassifyKeyword(ident);

                switch (state) {
                    case ScannerState.Default: {
                        if (keyword == KeywordKind.None && PeekColonAlias(query, i, out var colonPos)) {
                            Copy(ident, dest, ref pos);
                            Copy(query[i..(colonPos + 1)], dest, ref pos);
                            i = colonPos + 1;
                            break;
                        }

                        if (keyword == KeywordKind.None && i < query.Length && query[i] == (byte)'.') {
                            Copy(ident, dest, ref pos);
                            dest[pos++] = (byte)'.';
                            i++;
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.ExpectingTable;
                                break;

                            case KeywordKind.As:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.ExpectingAlias;
                                break;

                            case KeywordKind.With:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.ExpectingCTE;
                                break;

                            case KeywordKind.Lambda:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.AfterLambda;
                                break;

                            case KeywordKind.None:
                                ApplyColumnReplacement(ident, dest, ref pos);
                                break;

                            default:
                                Copy(ident, dest, ref pos);
                                break;
                        }

                        break;
                    }

                    case ScannerState.ExpectingTable: {
                        if (PeekColonAlias(query, i, out var colonPos)) {
                            Copy(ident, dest, ref pos);
                            Copy(query[i..(colonPos + 1)], dest, ref pos);
                            i = colonPos + 1;
                            break;
                        }

                        switch (keyword) {
                            case KeywordKind.JoinModifier:
                                Copy(ident, dest, ref pos);
                                break;

                            default:
                                if (_forbiddenTablePrefix is { } &&
                                    ident.Length >= _forbiddenTablePrefix.Length &&
                                    Ascii.EqualsIgnoreCase(ident[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                                    throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(ident)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                                ApplyTableReplacement(ident, dest, ref pos);
                                state = ScannerState.AfterTable;
                                break;
                        }

                        break;
                    }

                    case ScannerState.AfterTable:
                        switch (keyword) {
                            case KeywordKind.TableIntroducer:
                            case KeywordKind.JoinModifier:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.ExpectingTable;
                                break;

                            case KeywordKind.As:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.ExpectingTableAlias;
                                break;

                            default:
                                Copy(ident, dest, ref pos);
                                state = ScannerState.Default;
                                break;
                        }

                        break;

                    case ScannerState.ExpectingQualifiedTable: {
                        if (_forbiddenTablePrefix is { } &&
                            ident.Length >= _forbiddenTablePrefix.Length &&
                            Ascii.EqualsIgnoreCase(ident[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                            throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(ident)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                        ApplyTableReplacement(ident, dest, ref pos);
                        state = ScannerState.AfterTable;

                        break;
                    }

                    case ScannerState.ExpectingAlias:
                    case ScannerState.AfterCast:
                        Copy(ident, dest, ref pos);
                        state = ScannerState.Default;
                        break;

                    case ScannerState.ExpectingTableAlias:
                        Copy(ident, dest, ref pos);
                        state = ScannerState.AfterTable;
                        break;

                    case ScannerState.ExpectingCTE:
                        Copy(ident, dest, ref pos);
                        if (keyword != KeywordKind.Recursive)
                            state = ScannerState.AfterCTE;
                        break;

                    case ScannerState.AfterCTE:
                        Copy(ident, dest, ref pos);
                        state = ScannerState.Default;
                        break;

                    case ScannerState.AfterLambda:
                        Copy(ident, dest, ref pos);
                        break;
                }

                continue;
            }

            if (ch == (byte)':' && i + 1 < query.Length && query[i + 1] == (byte)':') {
                dest[pos++] = (byte)':';
                dest[pos++] = (byte)':';
                i     += 2;
                state =  ScannerState.AfterCast;
                continue;
            }

            if (ch == (byte)':' && state == ScannerState.AfterLambda) {
                dest[pos++] = (byte)':';
                i++;
                state = ScannerState.Default;
                continue;
            }

            if (ch == (byte)'.' && state == ScannerState.AfterTable) {
                dest[pos++] = (byte)'.';
                i++;
                state = ScannerState.ExpectingQualifiedTable;
                continue;
            }

            if (ch == (byte)',' && state == ScannerState.AfterTable) {
                dest[pos++] = ch;
                i++;
                state = ScannerState.ExpectingTable;
                continue;
            }

            if (ch == (byte)',' && state == ScannerState.AfterCTE) {
                dest[pos++] = ch;
                i++;
                state = ScannerState.ExpectingCTE;
                continue;
            }

            if (ch == (byte)'(') {
                if (depth >= 32)
                    throw new QueryNormalizationException("Nesting depth exceeds maximum of 32");

                stateStack[depth] = state;
                depth++;
                state = ScannerState.Default;
                dest[pos++] = ch;
                i++;
                continue;
            }

            if (ch == (byte)')') {
                if (depth > 0) {
                    depth--;
                    state = stateStack[depth];

                    if (state == ScannerState.ExpectingTable)
                        state = ScannerState.AfterTable;
                }

                dest[pos++] = ch;
                i++;
                continue;
            }

            if (ch >= (byte)'0' && ch <= (byte)'9') {
                var numRest = query[i..];
                var numEnd  = numRest.IndexOfAnyExcept(NumericBytes);
                var numLen  = numEnd < 0 ? numRest.Length : numEnd;
                numRest[..numLen].CopyTo(dest[pos..]);
                pos += numLen;
                i   += numLen;
                continue;
            }

            dest[pos++] = ch;
            i++;
        }
    }

    #region ->> Replacement <<-

    void ApplyColumnReplacement(ReadOnlySpan<byte> ident, Span<byte> dest, ref int pos) {
        if (_columnLookup.TryGetValue(ident, out var replacement)) {
            replacement.AsSpan().CopyTo(dest[pos..]);
            pos += replacement!.Length;
        }
        else {
            ident.CopyTo(dest[pos..]);
            pos += ident.Length;
        }
    }

    void ApplyTableReplacement(ReadOnlySpan<byte> ident, Span<byte> dest, ref int pos) {
        if (_tableLookup.TryGetValue(ident, out var replacement)) {
            replacement.AsSpan().CopyTo(dest[pos..]);
            pos += replacement!.Length;
        }
        else {
            ident.CopyTo(dest[pos..]);
            pos += ident.Length;
        }
    }

    #endregion

    #region ->> Double-quoted identifiers <<-

    void HandleDoubleQuotedIdentifier(ReadOnlySpan<byte> query, ref int i, Span<byte> dest, ref int pos, ref ScannerState state) {
        var start = i;
        i++; // skip opening "

        var hasEscapes = false;

        while (i < query.Length) {
            var dqRest = query[i..];
            var dqHit  = dqRest.IndexOf((byte)'"');

            if (dqHit < 0) {
                i = query.Length;
                break;
            }

            i += dqHit + 1;

            if (i < query.Length && query[i] == (byte)'"') {
                hasEscapes = true;
                i++;
                continue;
            }

            break;
        }

        var rawContent = query[(start + 1)..(i - 1)];

        ReadOnlySpan<byte> lookupContent;
        byte[]? unescapedArray = null;

        if (hasEscapes) {
            unescapedArray = new byte[rawContent.Length];
            var uLen = 0;

            for (var k = 0; k < rawContent.Length; k++) {
                unescapedArray[uLen++] = rawContent[k];

                if (rawContent[k] == (byte)'"' && k + 1 < rawContent.Length && rawContent[k + 1] == (byte)'"')
                    k++;
            }

            lookupContent = unescapedArray.AsSpan(0, uLen);
        }
        else
            lookupContent = rawContent;

        switch (state) {
            case ScannerState.Default: {
                if (_columnLookup.TryGetValue(lookupContent, out var replacement)) {
                    dest[pos++] = (byte)'"';
                    replacement.AsSpan().CopyTo(dest[pos..]);
                    pos += replacement!.Length;
                    dest[pos++] = (byte)'"';
                }
                else
                    Copy(query[start..i], dest, ref pos);

                break;
            }

            case ScannerState.ExpectingTable:
            case ScannerState.ExpectingQualifiedTable: {
                if (_forbiddenTablePrefix is { } &&
                    lookupContent.Length >= _forbiddenTablePrefix.Length &&
                    Ascii.EqualsIgnoreCase(lookupContent[.._forbiddenTablePrefix.Length], _forbiddenTablePrefix))
                    throw new QueryNormalizationException($"Table '{Encoding.UTF8.GetString(lookupContent)}' uses forbidden prefix '{Encoding.UTF8.GetString(_forbiddenTablePrefix)}'");

                if (_tableLookup.TryGetValue(lookupContent, out var replacement)) {
                    dest[pos++] = (byte)'"';
                    replacement.AsSpan().CopyTo(dest[pos..]);
                    pos += replacement!.Length;
                    dest[pos++] = (byte)'"';
                }
                else
                    Copy(query[start..i], dest, ref pos);

                state = ScannerState.AfterTable;
                break;
            }

            case ScannerState.ExpectingAlias:
            case ScannerState.AfterTable:
            case ScannerState.AfterCast:
                Copy(query[start..i], dest, ref pos);
                state = ScannerState.Default;
                break;

            case ScannerState.ExpectingTableAlias:
                Copy(query[start..i], dest, ref pos);
                state = ScannerState.AfterTable;
                break;

            default:
                Copy(query[start..i], dest, ref pos);
                break;
        }
    }

    #endregion

    #region ->> Skip regions <<-

    static void SkipLineComment(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        dest[pos++] = (byte)'-';
        dest[pos++] = (byte)'-';
        i += 2;
        var rest = q[i..];
        var nl   = rest.IndexOf((byte)'\n');

        if (nl < 0) {
            rest.CopyTo(dest[pos..]);
            pos += rest.Length;
            i    = q.Length;
        }
        else {
            rest[..(nl + 1)].CopyTo(dest[pos..]);
            pos += nl + 1;
            i   += nl + 1;
        }
    }

    static void SkipBlockComment(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        dest[pos++] = (byte)'/';
        dest[pos++] = (byte)'*';
        i += 2;
        var depth = 1;

        while (i < q.Length && depth > 0) {
            var rest = q[i..];
            var hit  = rest.IndexOfAny((byte)'*', (byte)'/');

            if (hit < 0) {
                rest.CopyTo(dest[pos..]);
                pos += rest.Length;
                i    = q.Length;
                return;
            }

            if (hit > 0) {
                rest[..hit].CopyTo(dest[pos..]);
                pos += hit;
                i   += hit;
            }

            if (i + 1 < q.Length && q[i] == (byte)'/' && q[i + 1] == (byte)'*') {
                dest[pos++] = (byte)'/';
                dest[pos++] = (byte)'*';
                i += 2;
                depth++;
            }
            else if (i + 1 < q.Length && q[i] == (byte)'*' && q[i + 1] == (byte)'/') {
                dest[pos++] = (byte)'*';
                dest[pos++] = (byte)'/';
                i += 2;
                depth--;
            }
            else {
                dest[pos++] = q[i];
                i++;
            }
        }
    }

    static void SkipStringLiteral(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        dest[pos++] = (byte)'\'';
        i++;

        while (i < q.Length) {
            var rest = q[i..];
            var hit  = rest.IndexOf((byte)'\'');

            if (hit < 0) {
                rest.CopyTo(dest[pos..]);
                pos += rest.Length;
                i    = q.Length;
                return;
            }

            if (hit > 0) {
                rest[..hit].CopyTo(dest[pos..]);
                pos += hit;
                i   += hit;
            }

            dest[pos++] = (byte)'\'';
            i++;

            if (i < q.Length && q[i] == (byte)'\'') {
                dest[pos++] = (byte)'\'';
                i++;
                continue;
            }

            return;
        }
    }

    static void SkipEscapeString(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        dest[pos++] = q[i]; // E or e
        i++;
        dest[pos++] = (byte)'\'';
        i++;

        while (i < q.Length) {
            var rest = q[i..];
            var hit  = rest.IndexOfAny((byte)'\\', (byte)'\'');

            if (hit < 0) {
                rest.CopyTo(dest[pos..]);
                pos += rest.Length;
                i    = q.Length;
                return;
            }

            if (hit > 0) {
                rest[..hit].CopyTo(dest[pos..]);
                pos += hit;
                i   += hit;
            }

            if (q[i] == (byte)'\\' && i + 1 < q.Length) {
                dest[pos++] = (byte)'\\';
                i++;
                dest[pos++] = q[i];
                i++;
                continue;
            }

            if (q[i] == (byte)'\'') {
                dest[pos++] = (byte)'\'';
                i++;

                if (i < q.Length && q[i] == (byte)'\'') {
                    dest[pos++] = (byte)'\'';
                    i++;
                    continue;
                }

                return;
            }

            dest[pos++] = q[i];
            i++;
        }
    }

    static void SkipDollarQuotedString(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        var start = i;
        i++;

        while (i < q.Length && q[i] != (byte)'$') {
            var c = q[i];

            if (IsAsciiLetter(c) || (c >= (byte)'0' && c <= (byte)'9') || c == (byte)'_')
                i++;
            else {
                i = start;
                dest[pos++] = q[i];
                i++;
                return;
            }
        }

        if (i >= q.Length) {
            i = start;
            dest[pos++] = q[i];
            i++;
            return;
        }

        i++;
        var delimiter = q[start..i];
        delimiter.CopyTo(dest[pos..]);
        pos += delimiter.Length;

        while (i <= q.Length - delimiter.Length) {
            var rest = q[i..];
            var hit  = rest.IndexOf((byte)'$');

            if (hit < 0) {
                rest.CopyTo(dest[pos..]);
                pos += rest.Length;
                i    = q.Length;
                return;
            }

            if (hit > 0) {
                rest[..hit].CopyTo(dest[pos..]);
                pos += hit;
                i   += hit;
            }

            if (i <= q.Length - delimiter.Length &&
                q.Slice(i, delimiter.Length).SequenceEqual(delimiter)) {
                delimiter.CopyTo(dest[pos..]);
                pos += delimiter.Length;
                i   += delimiter.Length;
                return;
            }

            dest[pos++] = q[i];
            i++;
        }

        var remaining = q[i..];
        remaining.CopyTo(dest[pos..]);
        pos += remaining.Length;
        i    = q.Length;
    }

    static void SkipBracket(ReadOnlySpan<byte> q, ref int i, Span<byte> dest, ref int pos) {
        dest[pos++] = (byte)'[';
        i++;
        var depth = 1;

        while (i < q.Length && depth > 0) {
            var rest = q[i..];
            var hit  = rest.IndexOfAny((byte)'[', (byte)']');

            if (hit < 0) {
                rest.CopyTo(dest[pos..]);
                pos += rest.Length;
                i    = q.Length;
                return;
            }

            if (hit > 0) {
                rest[..hit].CopyTo(dest[pos..]);
                pos += hit;
                i   += hit;
            }

            if (q[i] == (byte)'[')
                depth++;
            else if (q[i] == (byte)']')
                depth--;

            dest[pos++] = q[i];
            i++;
        }
    }

    #endregion

    #region ->> Helpers <<-

    static void Copy(ReadOnlySpan<byte> src, Span<byte> dest, ref int pos) {
        src.CopyTo(dest[pos..]);
        pos += src.Length;
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

    #endregion
}
