namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Metadata for a checkpoint blob stored in the blob store.
/// </summary>
/// <remarks>
/// <para>
/// Contains information about a stored checkpoint snapshot including
/// size, creation time, content type, and which partitions are included.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var metadata = new BlobMetadata {
///     Size            = parquetBytes.Length,
///     CreatedAt       = DateTimeOffset.UtcNow,
///     PartitionRanges = [PartitionRange.FromTo(0, 127)],
///     Checksum        = ComputeChecksum(parquetBytes)
/// };
///
/// await blobStore.StoreAsync(blobKey, parquetBytes, metadata);
/// </code>
/// </example>
[PublicAPI]
public record BlobMetadata {
    /// <summary>
    /// Size of the blob in bytes.
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// When the blob was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// MIME type of the blob content.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> "application/x-parquet+gzip"</para>
    /// <para>
    /// Indicates compressed Parquet format used for state snapshots.
    /// </para>
    /// </remarks>
    public string ContentType { get; init; } = "application/x-parquet+gzip";

    /// <summary>
    /// Partition ranges included in this blob.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> Empty array (all partitions)</para>
    /// <para>
    /// An empty array indicates the blob contains all partitions.
    /// Otherwise, contains the specific ranges included.
    /// </para>
    /// </remarks>
    public PartitionRange[] PartitionRanges { get; init; } = [];

    /// <summary>
    /// Optional checksum for data integrity verification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When present, can be used to verify blob integrity after retrieval.
    /// Typically a SHA-256 hash or similar.
    /// </para>
    /// </remarks>
    public string? Checksum { get; init; }

    /// <summary>
    /// Gets a value indicating whether this blob contains all partitions.
    /// </summary>
    public bool ContainsAllPartitions => PartitionRanges.Length == 0;

    /// <summary>
    /// Checks whether this blob contains data for the specified partition.
    /// </summary>
    /// <param name="partition">The partition number to check.</param>
    /// <returns><c>true</c> if the blob contains data for this partition; otherwise, <c>false</c>.</returns>
    public bool ContainsPartition(int partition) {
        if (ContainsAllPartitions)
            return true;

        foreach (var range in PartitionRanges) {
            if (range.Contains(partition))
                return true;
        }

        return false;
    }
}
