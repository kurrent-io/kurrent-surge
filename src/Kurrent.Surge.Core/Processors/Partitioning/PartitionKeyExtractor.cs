using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Processors.Partitioning;

/// <summary>
/// Extracts partition keys from records based on a property path expression.
/// </summary>
/// <remarks>
/// Used to determine which partition a record belongs to for parallel processing.
/// The extracted key is then hashed by <see cref="PartitionCalculator"/> to assign a partition number.
/// </remarks>
[PublicAPI]
public interface IPartitionKeyExtractor {
    /// <summary>
    /// Extracts the partition key value from a record.
    /// </summary>
    /// <param name="record">The record to extract the key from.</param>
    /// <returns>The partition key as a string, or <c>null</c> if the property doesn't exist.</returns>
    string? Extract(Record record);

    /// <summary>
    /// Gets the original expression used to create this extractor.
    /// </summary>
    string Expression { get; }
}

/// <summary>
/// Factory for creating partition key extractors from property path expressions.
/// </summary>
/// <example>
/// <code>
/// var extractor = PartitionKeyExtractor.Create("properties['tenantId']");
///
/// foreach (var record in records) {
///     var key = extractor.Extract(record);
///     if (key != null) {
///         var partition = PartitionCalculator.Default.For(key, maxParallelism: 128);
///     }
/// }
/// </code>
/// </example>
[PublicAPI]
public static class PartitionKeyExtractor {
    /// <summary>
    /// An extractor that always returns <c>null</c> (no partitioning).
    /// </summary>
    public static readonly IPartitionKeyExtractor None = new NullExtractor();

    /// <summary>
    /// Creates a partition key extractor from a property path expression.
    /// </summary>
    /// <param name="expression">The property path expression (e.g., "stream", "properties['userId']").</param>
    /// <returns>A partition key extractor that can extract values from records.</returns>
    /// <exception cref="ArgumentException">Thrown when the expression is invalid.</exception>
    public static IPartitionKeyExtractor Create(string expression) {
        if (string.IsNullOrWhiteSpace(expression))
            return None;

        if (!RecordFilterSchema.Default.TryBind(expression, out var read))
            throw new ArgumentException($"Invalid property path: '{expression}'", nameof(expression));

        return new PropertyPathExtractor(expression, read);
    }
}

file sealed class NullExtractor : IPartitionKeyExtractor {
    public string? Extract(Record record) => null;
    public string Expression => "";
}

/// <summary>
/// Reads a column bound once against <see cref="RecordFilterSchema.Default"/>. The schema declares
/// each column's text form, so the key does not depend on the value's in-memory type.
/// </summary>
file sealed class PropertyPathExtractor(string expression, Func<Record, string?> read) : IPartitionKeyExtractor {
    public string Expression => expression;

    public string? Extract(Record record) => read(record);
}
