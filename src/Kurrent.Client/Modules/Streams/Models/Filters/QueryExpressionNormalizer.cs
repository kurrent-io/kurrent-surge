// using System.Buffers;
// using System.Collections.Frozen;
// using System.Collections.Immutable;
// using System.Runtime.CompilerServices;
// using static System.StringComparison;
//
// namespace Kurrent.Client.Streams.Filters;
//
// public sealed record NormalizerOptions {
//     public ImmutableArray<string>                       ForbiddenTables      { get; init; } = [];
//     public ImmutableArray<KeyValuePair<string, string>> Replacements         { get; init; } = [];
//     public string?                                      ForbiddenTablePrefix { get; init; }
// }
//
// public readonly ref struct NormalizeResult {
//     public ReadOnlySpan<char> Value   { get; init; }
//     public bool               IsValid { get; init; }
//     public string?            Error   { get; init; }
//
//     public static NormalizeResult Success(ReadOnlySpan<char> normalized) =>
//         new() {
//             Value   = normalized,
//             IsValid = true
//         };
//
//     public static NormalizeResult Failure(string error) =>
//         new() {
//             Value   = ReadOnlySpan<char>.Empty,
//             IsValid = false,
//             Error   = error
//         };
// }
//
// public sealed class QueryExpressionNormalizer {
//     const int MaxStackAlloc = 256;
//     const int MaxStackDepth = 32;
//
//     readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>>                _forbiddenLookup;
//     readonly FrozenDictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _replacementLookup;
//
//     readonly string? _forbiddenPrefix;
//
//     public QueryExpressionNormalizer(NormalizerOptions options) {
//         _forbiddenLookup = options.ForbiddenTables
//             .ToFrozenSet(StringComparer.OrdinalIgnoreCase)
//             .GetAlternateLookup<ReadOnlySpan<char>>();
//
//         _replacementLookup = options.Replacements
//             .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
//             .GetAlternateLookup<ReadOnlySpan<char>>();
//
//         _forbiddenPrefix = options.ForbiddenTablePrefix;
//     }
//
//     /// <summary>
//     /// Validates and rewrites a query in a single pass.
//     /// Returns immediately on the first violation.
//     /// Buffer must be large enough — query.Length * 2 is safe.
//     /// </summary>
//     public NormalizeResult Normalize(ReadOnlySpan<char> query, Span<char> buffer) {
//         string? error;
//         int     written;
//
//         if (!query.Contains('\\')) {
//             error = Scan(query, buffer, out written);
//
//             return error is null
//                 ? NormalizeResult.Success(buffer[..written])
//                 : NormalizeResult.Failure(error);
//         }
//
//         char[]? rented = null;
//
//         var temp = query.Length <= MaxStackAlloc
//             ? stackalloc char[MaxStackAlloc]
//             : rented = ArrayPool<char>.Shared.Rent(query.Length);
//
//         try {
//             var len = DecodeUnicodeEscapes(query, temp);
//             error = Scan(temp[..len], buffer, out written);
//         }
//         finally {
//             if (rented is { })
//                 ArrayPool<char>.Shared.Return(rented);
//         }
//
//         return error is null
//             ? NormalizeResult.Success(buffer[..written])
//             : NormalizeResult.Failure(error);
//
//         static int DecodeUnicodeEscapes(ReadOnlySpan<char> input, Span<char> output) {
//             var w = 0;
//             var i = 0;
//
//             while (i < input.Length)
//                 if (input[i] == '\\' && TryDecodeUnicodeEscape(input, i, out var decoded)) {
//                     output[w++] =  decoded;
//                     i           += 6;
//                 }
//                 else
//                     output[w++] = input[i++];
//
//             return w;
//
//             [MethodImpl(MethodImplOptions.AggressiveInlining)]
//             static bool TryDecodeUnicodeEscape(ReadOnlySpan<char> query, int pos, out char decoded) {
//                 decoded = '\0';
//
//                 if (pos + 5 >= query.Length)
//                     return false;
//
//                 if (query[pos + 1] is not ('u' or 'U'))
//                     return false;
//
//                 var hex = query.Slice(pos + 2, 4);
//
//                 return ushort.TryParse(
//                            hex, System.Globalization.NumberStyles.HexNumber, null,
//                            out var value
//                        )
//                     && (decoded = (char)value) is not '\0';
//             }
//         }
//     }
//
//     string? Scan(ReadOnlySpan<char> input, Span<char> output, out int written) {
//         Span<State> stateStack = stackalloc State[MaxStackDepth];
//
//         written = 0;
//
//         var state = State.Default;
//         var depth = 0;
//         var i     = 0;
//
//         while (i < input.Length) {
//             var c = input[i];
//
//             switch (c) {
//                 case '-' when Peek(input, i + 1) == '-': {
//                     var end = SkipLineComment(input, i + 2);
//
//                     CopyRange(
//                         input, i, end,
//                         output, ref written
//                     );
//
//                     i = end;
//
//                     break;
//                 }
//
//                 case '/' when Peek(input, i + 1) == '*': {
//                     var end = SkipBlockComment(input, i + 2);
//
//                     CopyRange(
//                         input, i, end,
//                         output, ref written
//                     );
//
//                     i = end;
//
//                     break;
//                 }
//
//                 case '\'': {
//                     var end = SkipStringLiteral(input, i + 1);
//
//                     CopyRange(
//                         input, i, end,
//                         output, ref written
//                     );
//
//                     i = end;
//
//                     break;
//                 }
//
//                 case 'N' or 'n' when Peek(input, i + 1) == '\'': {
//                     var end = SkipStringLiteral(input, i + 2);
//
//                     CopyRange(
//                         input, i, end,
//                         output, ref written
//                     );
//
//                     i = end;
//
//                     break;
//                 }
//
//                 case '(':
//                     if (!PushState(stateStack, ref depth, state))
//                         return "Query exceeds maximum nesting depth";
//
//                     state             = State.Default;
//                     output[written++] = c;
//                     i++;
//
//                     break;
//
//                 case ')':
//                     state             = PopState(stateStack, ref depth);
//                     output[written++] = c;
//                     i++;
//
//                     break;
//
//                 case ',':
//                     if (state is State.AfterTable)
//                         state = State.ExpectingTable;
//
//                     output[written++] = c;
//                     i++;
//
//                     break;
//
//                 case '[': {
//                     var error = HandleDelimited(
//                         input, i, ']',
//                         state, output, ref written,
//                         out var end
//                     );
//
//                     if (error is { })
//                         return error;
//
//                     state = AdvanceState(state);
//                     i     = end;
//
//                     break;
//                 }
//
//                 case '"': {
//                     var error = HandleDelimited(
//                         input, i, '"',
//                         state, output, ref written,
//                         out var end
//                     );
//
//                     if (error is { })
//                         return error;
//
//                     state = AdvanceState(state);
//                     i     = end;
//
//                     break;
//                 }
//
//                 default:
//                     if (IsIdentifierStart(c)) {
//                         var start = i++;
//
//                         while (i < input.Length && IsIdentifierPart(input[i]))
//                             i++;
//
//                         var ident     = input[start..i];
//                         var nextState = TransitionKeyword(ident, state);
//
//                         var error = ResolveIdentifier(
//                             ident, state, input,
//                             start, i,
//                             output, ref written
//                         );
//
//                         if (error is { })
//                             return error;
//
//                         state = nextState;
//                     }
//                     else {
//                         output[written++] = c;
//                         i++;
//                     }
//
//                     break;
//             }
//         }
//
//         return null;
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         static State TransitionKeyword(ReadOnlySpan<char> ident, State state) {
//             return (state, ClassifyKeyword(ident)) switch {
//                 (_, Keyword.From or Keyword.Join or Keyword.Into or Keyword.Update or Keyword.Table) => State.ExpectingTable,
//                 (State.ExpectingTable, Keyword.JoinModifier)                                         => State.ExpectingTable,
//                 (State.AfterTable, Keyword.As)                                                       => State.ExpectingAlias,
//                 (_, Keyword.Resetting)                                                               => State.Default,
//                 (State.ExpectingTable, Keyword.None)                                                 => State.AfterTable,
//                 (State.AfterTable, Keyword.None)                                                     => State.Default,
//                 (State.ExpectingAlias, Keyword.None)                                                 => State.Default,
//
//                 _ => state
//             };
//
//             [MethodImpl(MethodImplOptions.AggressiveInlining)]
//             static Keyword ClassifyKeyword(ReadOnlySpan<char> ident) {
//
//                 // this looks terrible, we should have one local func per case
//                 return ident.Length switch {
//                     2 => ident.Equals("AS", OrdinalIgnoreCase)
//                         ? Keyword.As
//                         : ident.Equals("ON", OrdinalIgnoreCase)
//                             ? Keyword.Resetting
//                             : Keyword.None,
//
//                     3 => ident.Equals("SET", OrdinalIgnoreCase)
//                         ? Keyword.Resetting
//                         : Keyword.None,
//
//                     4 => ident.Equals("FROM", OrdinalIgnoreCase)
//                         ? Keyword.From
//                         : ident.Equals("JOIN", OrdinalIgnoreCase)
//                             ? Keyword.Join
//                             : ident.Equals("INTO", OrdinalIgnoreCase)
//                                 ? Keyword.Into
//                                 : ident.Equals("FULL", OrdinalIgnoreCase)
//                                     ? Keyword.JoinModifier
//                                     : ident.Equals("LEFT", OrdinalIgnoreCase)
//                                         ? Keyword.JoinModifier
//                                         : Keyword.None,
//
//                     5 => ident.Equals("TABLE", OrdinalIgnoreCase)
//                         ? Keyword.Table
//                         : ident.Equals("WHERE", OrdinalIgnoreCase)
//                             ? Keyword.Resetting
//                             : ident.Equals("ORDER", OrdinalIgnoreCase)
//                                 ? Keyword.Resetting
//                                 : ident.Equals("GROUP", OrdinalIgnoreCase)
//                                     ? Keyword.Resetting
//                                     : ident.Equals("LIMIT", OrdinalIgnoreCase)
//                                         ? Keyword.Resetting
//                                         : ident.Equals("UNION", OrdinalIgnoreCase)
//                                             ? Keyword.Resetting
//                                             : ident.Equals("CROSS", OrdinalIgnoreCase)
//                                                 ? Keyword.JoinModifier
//                                                 : ident.Equals("INNER", OrdinalIgnoreCase)
//                                                     ? Keyword.JoinModifier
//                                                     : ident.Equals("OUTER", OrdinalIgnoreCase)
//                                                         ? Keyword.JoinModifier
//                                                         : ident.Equals("RIGHT", OrdinalIgnoreCase)
//                                                             ? Keyword.JoinModifier
//                                                             : Keyword.None,
//
//                     6 => ident.Equals("UPDATE", OrdinalIgnoreCase)
//                         ? Keyword.Update
//                         : ident.Equals("HAVING", OrdinalIgnoreCase)
//                             ? Keyword.Resetting
//                             : ident.Equals("VALUES", OrdinalIgnoreCase)
//                                 ? Keyword.Resetting
//                                 : ident.Equals("SELECT", OrdinalIgnoreCase)
//                                     ? Keyword.Resetting
//                                     : Keyword.None,
//
//                     7 => ident.Equals("LATERAL", OrdinalIgnoreCase)
//                         ? Keyword.JoinModifier
//                         : ident.Equals("NATURAL", OrdinalIgnoreCase)
//                             ? Keyword.JoinModifier
//                             : Keyword.None,
//
//                     9 => ident.Equals("RETURNING", OrdinalIgnoreCase)
//                         ? Keyword.Resetting
//                         : Keyword.None,
//
//                     _ => Keyword.None
//                 };
//             }
//         }
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch == '_';
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         static int SkipLineComment(ReadOnlySpan<char> q, int from) {
//             var idx = q[from..].IndexOf('\n');
//
//             return idx < 0 ? q.Length : from + idx + 1;
//         }
//
//         static int SkipBlockComment(ReadOnlySpan<char> q, int from) {
//             var j = from;
//
//             while (j < q.Length) {
//                 var idx = q[j..].IndexOf('*');
//
//                 if (idx < 0)
//                     return q.Length;
//
//                 j += idx + 1;
//
//                 if ((uint)j < (uint)q.Length && q[j] == '/')
//                     return j + 1;
//             }
//
//             return q.Length;
//         }
//
//         static int SkipStringLiteral(ReadOnlySpan<char> q, int from) {
//             var j = from;
//
//             while (j < q.Length) {
//                 var idx = q[j..].IndexOf('\'');
//
//                 if (idx < 0)
//                     return q.Length;
//
//                 j += idx + 1;
//
//                 if ((uint)j < (uint)q.Length && q[j] == '\'') {
//                     j++;
//
//                     continue;
//                 }
//
//                 return j;
//             }
//
//             return q.Length;
//         }
//     }
//
//     /// <summary>
//     /// Unified check-forbidden → try-replace → copy-or-write for all identifier forms.
//     /// When quoteOpen/quoteClose are non-zero, replacement output is wrapped in those chars.
//     /// On no-replacement, the original region (source[regionStart..regionEnd]) is copied verbatim.
//     /// </summary>
//     string? ResolveIdentifier(
//         ReadOnlySpan<char> identifier,
//         State state,
//         ReadOnlySpan<char> source,
//         int regionStart,
//         int regionEnd,
//         Span<char> output,
//         ref int written,
//         char quoteOpen = '\0',
//         char quoteClose = '\0'
//     ) {
//         var error = CheckForbidden(identifier, state);
//
//         if (error is { })
//             return error;
//
//         if (_replacementLookup.TryGetValue(identifier, out var replacement)) {
//             if (quoteOpen != '\0')
//                 output[written++] = quoteOpen;
//
//             replacement.AsSpan().CopyTo(output[written..]);
//             written += replacement.Length;
//
//             if (quoteClose != '\0')
//                 output[written++] = quoteClose;
//         }
//         else {
//             var slice = source[regionStart..regionEnd];
//             slice.CopyTo(output[written..]);
//             written += slice.Length;
//         }
//
//         return null;
//
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         string? CheckForbidden(ReadOnlySpan<char> identifier, State state) {
//             if (_forbiddenLookup.Contains(identifier))
//                 return $"Forbidden table: '{identifier}'";
//
//             if (_forbiddenPrefix is { } && state is State.ExpectingTable
//                                         && identifier.StartsWith(_forbiddenPrefix, OrdinalIgnoreCase))
//                 return $"Table matches forbidden prefix '{_forbiddenPrefix}': '{identifier}'";
//
//             return null;
//         }
//     }
//
//     string? HandleDelimited(
//         ReadOnlySpan<char> input,
//         int openPos,
//         char closeChar,
//         State state,
//         Span<char> output,
//         ref int written,
//         out int resumeAt
//     ) {
//         var openChar  = input[openPos];
//         var start     = openPos + 1;
//         var i         = start;
//         var hasEscape = false;
//
//         while (i < input.Length) {
//             var idx = input[i..].IndexOf(closeChar);
//
//             if (idx < 0)
//                 break;
//
//             i += idx;
//
//             if (Peek(input, i + 1) == closeChar) {
//                 hasEscape =  true;
//                 i         += 2;
//
//                 continue;
//             }
//
//             resumeAt = i + 1;
//             var raw = input[start..i];
//
//             if (!hasEscape)
//                 return ResolveIdentifier(
//                     raw, state, input,
//                     openPos, resumeAt,
//                     output, ref written, openChar,
//                     closeChar
//                 );
//
//             return ResolveEscapedIdentifier(
//                 raw, closeChar, state,
//                 input, openPos, resumeAt,
//                 openChar, closeChar, output,
//                 ref written
//             );
//         }
//
//         // unterminated — copy verbatim
//         CopyRange(
//             input, openPos, input.Length,
//             output, ref written
//         );
//
//         resumeAt = input.Length;
//
//         return null;
//     }
//
//     string? ResolveEscapedIdentifier(
//         ReadOnlySpan<char> raw,
//         char escapeChar,
//         State state,
//         ReadOnlySpan<char> input,
//         int regionStart,
//         int regionEnd,
//         char quoteOpen,
//         char quoteClose,
//         Span<char> output,
//         ref int written
//     ) {
//         char[]? rented = null;
//
//         var buffer = raw.Length <= MaxStackAlloc
//             ? stackalloc char[MaxStackAlloc]
//             : rented = ArrayPool<char>.Shared.Rent(raw.Length);
//
//         try {
//             var len = Unescape(raw, escapeChar, buffer);
//
//             return ResolveIdentifier(
//                 buffer[..len], state, input,
//                 regionStart, regionEnd,
//                 output, ref written, quoteOpen,
//                 quoteClose
//             );
//         }
//         finally {
//             if (rented is { })
//                 ArrayPool<char>.Shared.Return(rented);
//         }
//     }
//
//     /// <summary>
//     /// Advances state after a delimited identifier (which cannot be a keyword).
//     /// </summary>
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static State AdvanceState(State state) =>
//         state switch {
//             State.ExpectingTable => State.AfterTable,
//             State.AfterTable     => State.Default,
//             State.ExpectingAlias => State.Default,
//             _                    => state
//         };
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static bool PushState(Span<State> stack, ref int depth, State current) {
//         if ((uint)depth >= (uint)stack.Length)
//             return false;
//
//         stack[depth++] = current;
//
//         return true;
//     }
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static State PopState(Span<State> stack, ref int depth) => depth > 0 ? stack[--depth] : State.Default;
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static char Peek(ReadOnlySpan<char> query, int index) => (uint)index < (uint)query.Length ? query[index] : '\0';
//
//     [MethodImpl(MethodImplOptions.AggressiveInlining)]
//     static void CopyRange(ReadOnlySpan<char> source, int from, int to, Span<char> dest, ref int written) {
//         var slice = source[from..to];
//         slice.CopyTo(dest[written..]);
//         written += slice.Length;
//     }
//
//     static int Unescape(ReadOnlySpan<char> raw, char escapeChar, Span<char> buffer) {
//         var w = 0;
//
//         for (var i = 0; i < raw.Length; i++) {
//             buffer[w++] = raw[i];
//
//             if (raw[i] == escapeChar &&
//                 (uint)(i + 1) < (uint)raw.Length &&
//                 raw[i + 1] == escapeChar)
//                 i++;
//         }
//
//         return w;
//     }
//
//     enum State : byte {
//         Default,
//         ExpectingTable,
//         AfterTable,
//         ExpectingAlias
//     }
//
//     enum Keyword : byte {
//         None,
//         From,
//         Join,
//         Into,
//         Update,
//         Table,
//         As,
//         JoinModifier,
//         Resetting
//     }
// }
