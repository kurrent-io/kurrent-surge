using System.Diagnostics.CodeAnalysis;

namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Structured key for identifying checkpoint blobs in storage.
/// </summary>
/// <remarks>
/// <para>
/// Combines a processor identifier with a checkpoint identifier to form
/// a unique key for storing and retrieving state snapshots.
/// </para>
/// <para>
/// The string format is <c>processor-{ProcessorId}/checkpoint-{CheckpointId}</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var key = new BlobKey("order-processor", 12345);
/// Console.WriteLine(key); // "processor-order-processor/checkpoint-12345"
///
/// // Parse from string
/// if (BlobKey.TryParse("processor-order-processor/checkpoint-12345", out var parsed)) {
///     Console.WriteLine(parsed.ProcessorId);   // "order-processor"
///     Console.WriteLine(parsed.CheckpointId);  // 12345
/// }
/// </code>
/// </example>
/// <param name="ProcessorId">The unique identifier of the processor.</param>
/// <param name="CheckpointId">The checkpoint sequence number.</param>
[PublicAPI]
public readonly record struct BlobKey(string ProcessorId, long CheckpointId) {
    const string ProcessorPrefix   = "processor-";
    const string CheckpointPrefix  = "/checkpoint-";

    /// <summary>
    /// Returns the string representation of this blob key.
    /// </summary>
    /// <returns>A string in the format "processor-{ProcessorId}/checkpoint-{CheckpointId}".</returns>
    public override string ToString() => $"{ProcessorPrefix}{ProcessorId}{CheckpointPrefix}{CheckpointId}";

    /// <summary>
    /// Parses a string into a <see cref="BlobKey"/>.
    /// </summary>
    /// <param name="key">The string to parse.</param>
    /// <returns>The parsed <see cref="BlobKey"/>.</returns>
    /// <exception cref="FormatException">Thrown when the string format is invalid.</exception>
    public static BlobKey Parse(string key) {
        if (!TryParse(key, out var result))
            throw new FormatException($"Invalid blob key format: '{key}'. Expected format: 'processor-{{processorId}}/checkpoint-{{checkpointId}}'");

        return result;
    }

    /// <summary>
    /// Attempts to parse a string into a <see cref="BlobKey"/>.
    /// </summary>
    /// <param name="key">The string to parse.</param>
    /// <param name="result">When successful, contains the parsed <see cref="BlobKey"/>.</param>
    /// <returns><c>true</c> if parsing succeeded; otherwise, <c>false</c>.</returns>
    public static bool TryParse(string key, [NotNullWhen(true)] out BlobKey result) {
        result = default;

        if (string.IsNullOrEmpty(key))
            return false;

        if (!key.StartsWith(ProcessorPrefix, StringComparison.Ordinal))
            return false;

        var checkpointIndex = key.IndexOf(CheckpointPrefix, StringComparison.Ordinal);
        if (checkpointIndex < 0)
            return false;

        var processorId     = key[ProcessorPrefix.Length..checkpointIndex];
        var checkpointIdStr = key[(checkpointIndex + CheckpointPrefix.Length)..];

        if (string.IsNullOrEmpty(processorId))
            return false;

        if (!long.TryParse(checkpointIdStr, out var checkpointId))
            return false;

        result = new BlobKey(processorId, checkpointId);
        return true;
    }

    /// <summary>
    /// Creates a blob key for the first checkpoint of a processor.
    /// </summary>
    /// <param name="processorId">The processor identifier.</param>
    /// <returns>A <see cref="BlobKey"/> with checkpoint ID 0.</returns>
    public static BlobKey Initial(string processorId) => new(processorId, 0);

    /// <summary>
    /// Returns the next checkpoint key in sequence.
    /// </summary>
    /// <returns>A new <see cref="BlobKey"/> with the checkpoint ID incremented by 1.</returns>
    public BlobKey Next() => this with { CheckpointId = CheckpointId + 1 };
}
