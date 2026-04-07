// using System.Collections.Immutable;
// using System.Text;
// using Kurrent.Client.Streams;
//
// namespace Kurrent.Client.Processors.Partitioning;
//
// /// <summary>
// /// Extracts partition keys from records based on a property path expression.
// /// </summary>
// /// <remarks>
// /// <para>
// /// Used to determine which partition a record belongs to for parallel processing.
// /// The extracted key is then hashed by <see cref="PartitionCalculator"/> to assign a partition number.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public interface IPartitionKeyExtractor {
//     /// <summary>
//     /// Extracts the partition key value from a record.
//     /// </summary>
//     /// <param name="record">The record to extract the key from.</param>
//     /// <returns>The partition key as a string, or <c>null</c> if the property doesn't exist.</returns>
//     string? Extract(Record record);
//
//     /// <summary>
//     /// Gets the original expression used to create this extractor.
//     /// </summary>
//     string Expression { get; }
// }
//
// /// <summary>
// /// Factory for creating partition key extractors from property path expressions.
// /// </summary>
// /// <remarks>
// /// <para>
// /// Supported expression formats:
// /// <list type="bullet">
// ///   <item><c>stream</c> - The stream name</item>
// ///   <item><c>schema</c> or <c>schema_name</c> - The schema/event type name</item>
// ///   <item><c>position</c> or <c>pos</c> - The log position</item>
// ///   <item><c>timestamp</c> or <c>ts</c> - The record timestamp</item>
// ///   <item><c>stream_revision</c> or <c>revision</c> - The stream revision</item>
// ///   <item><c>record_id</c> or <c>id</c> - The record unique identifier</item>
// ///   <item><c>properties['key']</c> or <c>props['key']</c> - Metadata value by bracket notation</item>
// ///   <item><c>properties.key</c> or <c>props.key</c> - Metadata value by dot notation</item>
// /// </list>
// /// </para>
// /// </remarks>
// /// <example>
// /// <code>
// /// // Create extractor from expression
// /// var extractor = PartitionKeyExtractor.Create("properties['tenantId']");
// ///
// /// // Extract key from records
// /// foreach (var record in records) {
// ///     var key = extractor.Extract(record);
// ///     if (key != null) {
// ///         var partition = PartitionCalculator.Default.For(key, maxParallelism: 128);
// ///     }
// /// }
// /// </code>
// /// </example>
// [PublicAPI]
// public static class PartitionKeyExtractor {
//     /// <summary>
//     /// An extractor that always returns <c>null</c> (no partitioning).
//     /// </summary>
//     public static readonly IPartitionKeyExtractor None = new NullExtractor();
//
//     /// <summary>
//     /// Creates a partition key extractor from a property path expression.
//     /// </summary>
//     /// <param name="expression">The property path expression (e.g., "stream", "properties['userId']").</param>
//     /// <returns>A partition key extractor that can extract values from records.</returns>
//     /// <exception cref="ArgumentException">Thrown when the expression is invalid.</exception>
//     public static IPartitionKeyExtractor Create(string expression) {
//         if (string.IsNullOrWhiteSpace(expression))
//             return None;
//
//         var parseResult = PropertyPathParser.Parse(expression);
//
//         if (!parseResult.IsSuccess)
//             throw new ArgumentException(parseResult.Error!.ErrorMessage, nameof(expression));
//
//         return new PropertyPathExtractor(expression, parseResult.Value!);
//     }
//
//     /// <summary>
//     /// Tries to create a partition key extractor from a property path expression.
//     /// </summary>
//     /// <param name="expression">The property path expression.</param>
//     /// <returns>A result containing the extractor or an error.</returns>
//     public static Result<IPartitionKeyExtractor, PartitionKeyParseError> TryCreate(string expression) {
//         if (string.IsNullOrWhiteSpace(expression))
//             return Result.Success<IPartitionKeyExtractor, PartitionKeyParseError>(None);
//
//         var parseResult = PropertyPathParser.Parse(expression);
//
//         return parseResult.IsSuccess
//             ? Result.Success<IPartitionKeyExtractor, PartitionKeyParseError>(
//                 new PropertyPathExtractor(expression, parseResult.Value!))
//             : Result.Failure<IPartitionKeyExtractor, PartitionKeyParseError>(parseResult.Error!);
//     }
// }
//
// /// <summary>
// /// Represents an error that occurred while parsing a partition key expression.
// /// </summary>
// /// <remarks>
// /// <para>
// /// This error provides structured metadata including the position and token where parsing failed,
// /// enabling precise error reporting and diagnostics.
// /// </para>
// /// </remarks>
// [PublicAPI]
// public sealed record PartitionKeyParseError : IResultError {
//     /// <inheritdoc />
//     public ErrorCode ErrorCode { get; } = ErrorCode.FromType<PartitionKeyParseError>();
//
//     /// <inheritdoc />
//     public string ErrorMessage { get; }
//
//     /// <summary>
//     /// Gets the zero-based position in the input where the error occurred.
//     /// </summary>
//     public int Position { get; }
//
//     /// <summary>
//     /// Gets the token or character that caused the parse error, if available.
//     /// </summary>
//     public string? NearToken { get; }
//
//     /// <inheritdoc />
//     public ImmutableDictionary<string, object?> Metadata { get; }
//
//     PartitionKeyParseError(string errorMessage, int position, string? nearToken = null) {
//         ErrorMessage = errorMessage;
//         Position     = position;
//         NearToken    = nearToken;
//
//         var builder = ImmutableDictionary.CreateBuilder<string, object?>();
//         builder["Position"] = position;
//
//         if (nearToken is not null)
//             builder["NearToken"] = nearToken;
//
//         Metadata = builder.ToImmutable();
//     }
//
//     internal static PartitionKeyParseError SyntaxError(string nearToken, int position) =>
//         new($"Partition key parse error at position {position}: unexpected token '{nearToken}'", position, nearToken);
//
//     internal static PartitionKeyParseError EmptyInput() =>
//         new("Partition key parse error: expression cannot be empty or whitespace", 0);
//
//     /// <inheritdoc />
//     public Exception GetOrCreateException() =>
//         new FormatException(ErrorMessage);
// }
//
// /// <summary>
// /// Null extractor that always returns <c>null</c> regardless of input.
// /// Used when no partition key expression is specified.
// /// </summary>
// file sealed class NullExtractor : IPartitionKeyExtractor {
//     /// <inheritdoc />
//     public string? Extract(Record record) => null;
//
//     /// <inheritdoc />
//     public string Expression => string.Empty;
// }
//
// /// <summary>
// /// Extractor that uses a parsed <see cref="PropertyAccess"/> to extract values from records.
// /// Converts extracted values to strings suitable for consistent hashing.
// /// </summary>
// file sealed class PropertyPathExtractor(string expression, PropertyAccess property) : IPartitionKeyExtractor {
//     /// <inheritdoc />
//     public string Expression => expression;
//
//     /// <inheritdoc />
//     public string? Extract(Record record) {
//         ArgumentNullException.ThrowIfNull(record);
//
//         var value = property.Resolve(record);
//         return value switch {
//             null        => null,
//             string s    => s,
//             Guid g      => g.ToString("N"), // No dashes for consistent hashing
//             DateTime dt => dt.ToString("O"),
//             _           => value.ToString()
//         };
//     }
// }
//
// /// <summary>
// /// Simple parser for property path expressions.
// /// </summary>
// /// <remarks>
// /// <para>
// /// This parser shares the <see cref="PropertyAccess"/> type with <c>ExpressionFilterParser</c>
// /// but duplicates the parsing logic. This duplication exists because:
// /// <list type="bullet">
// ///   <item>This parser only needs property path parsing (no comparison operators, logical operators)</item>
// ///   <item>ExpressionFilterParser's tokenizer is tightly coupled to its full expression grammar</item>
// /// </list>
// /// </para>
// /// <para>
// /// <b>Future Refactoring:</b> Consider extracting a shared <c>PropertyPathTokenizer</c> that both
// /// parsers can use, eliminating the duplicated identifier/bracket/dot parsing logic.
// /// </para>
// /// </remarks>
// file static class PropertyPathParser {
//     public static Result<PropertyAccess, PartitionKeyParseError> Parse(string input) {
//         if (string.IsNullOrWhiteSpace(input))
//             return PartitionKeyParseError.EmptyInput();
//
//         var span = input.AsSpan().Trim();
//         if (span.IsEmpty)
//             return PartitionKeyParseError.EmptyInput();
//
//         try {
//             var parser = new Parser(span.ToString());
//             return Result.Success<PropertyAccess, PartitionKeyParseError>(parser.ParsePropertyPath());
//         }
//         catch (ParseException ex) {
//             return PartitionKeyParseError.SyntaxError(ex.NearToken, ex.Position);
//         }
//     }
//
//     sealed class ParseException(string nearToken, int position) : Exception {
//         public string NearToken { get; } = nearToken;
//         public int    Position  { get; } = position;
//     }
//
//     sealed class Parser(string input) {
//         int _position;
//
//         char Current => _position < input.Length ? input[_position] : '\0';
//         bool IsAtEnd => _position >= input.Length;
//
//         public PropertyAccess ParsePropertyPath() {
//             SkipWhitespace();
//
//             var identifier = ReadIdentifier();
//             if (string.IsNullOrEmpty(identifier))
//                 throw new ParseException("identifier", _position);
//
//             SkipWhitespace();
//             string? key = null;
//
//             if (!IsAtEnd) {
//                 if (Current == '[') {
//                     key = ReadBracketKey();
//                 }
//                 else if (Current == '.') {
//                     key = ReadDotKey();
//                 }
//             }
//
//             SkipWhitespace();
//             if (!IsAtEnd)
//                 throw new ParseException(Current.ToString(), _position);
//
//             return new PropertyAccess(identifier, key);
//         }
//
//         string ReadIdentifier() {
//             var start = _position;
//
//             if (!char.IsLetter(Current) && Current != '_')
//                 return string.Empty;
//
//             while (!IsAtEnd && (char.IsLetterOrDigit(Current) || Current == '_'))
//                 _position++;
//
//             return input[start.._position];
//         }
//
//         string ReadBracketKey() {
//             _position++; // Skip '['
//             SkipWhitespace();
//
//             if (Current != '\'')
//                 throw new ParseException("'", _position);
//
//             _position++; // Skip opening quote
//
//             var sb = new StringBuilder(32); // Initial capacity for typical key length
//
//             while (!IsAtEnd && Current != '\'') {
//                 if (Current == '\\' && _position + 1 < input.Length) {
//                     _position++;
//                     sb.Append(Current);
//                 }
//                 else {
//                     sb.Append(Current);
//                 }
//                 _position++;
//             }
//
//             if (Current != '\'')
//                 throw new ParseException("'", _position);
//
//             _position++; // Skip closing quote
//             SkipWhitespace();
//
//             if (Current != ']')
//                 throw new ParseException("]", _position);
//
//             _position++; // Skip ']'
//
//             return sb.ToString();
//         }
//
//         string ReadDotKey() {
//             _position++; // Skip '.'
//             SkipWhitespace();
//
//             var key = ReadIdentifier();
//             if (string.IsNullOrEmpty(key))
//                 throw new ParseException("identifier", _position);
//
//             return key;
//         }
//
//         void SkipWhitespace() {
//             while (!IsAtEnd && char.IsWhiteSpace(Current))
//                 _position++;
//         }
//     }
// }
