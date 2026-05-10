using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Partitioning;

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

        return new PropertyPathExtractor(expression, PropertyAccess.Parse(expression));
    }
}

file sealed class NullExtractor : IPartitionKeyExtractor {
    public string? Extract(Record record) => null;
    public string Expression => "";
}

/// <summary>
/// Resolves a <see cref="PropertyAccess"/> against <see cref="RecordPropertyMap.Default"/>
/// and formats the result as a string suitable for consistent hashing.
/// </summary>
file sealed class PropertyPathExtractor(string expression, PropertyAccess property) : IPartitionKeyExtractor {
    public string Expression => expression;

    public string? Extract(Record record) {
        ArgumentNullException.ThrowIfNull(record);

        var value = RecordPropertyMap.Default.
            Resolve(property.PropertyName, property.Key, record);
        
        return value switch {
            null        => null,
            string s    => s,
            Guid g      => g.ToString("N"),
            DateTime dt => dt.ToString("O"),
            _           => value.ToString()
        };
    }
}
