namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Interface for storing and retrieving checkpoint blobs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Only:</b> This interface defines the contract for checkpoint blob storage.
/// Implementations may include local file storage, cloud storage (S3, Azure Blob, GCS),
/// or distributed storage systems.
/// </para>
/// <para>
/// The blob store is used by the processor framework to persist state snapshots
/// created by <see cref="ICheckpointStateStore"/>. Each checkpoint is stored as
/// a compressed Parquet file identified by a <see cref="BlobKey"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Store a checkpoint
/// var blobKey  = new BlobKey(processorId, checkpointId);
/// var metadata = new BlobMetadata {
///     Size            = parquetBytes.Length,
///     CreatedAt       = DateTimeOffset.UtcNow,
///     PartitionRanges = [PartitionRange.FromTo(0, 127)]
/// };
///
/// await blobStore.StoreAsync(blobKey, parquetBytes, metadata);
///
/// // Retrieve a checkpoint
/// var retrieved = await blobStore.RetrieveAsync(blobKey);
/// if (retrieved.HasValue) {
///     await checkpointStore.RestoreAsync(retrieved.Value);
/// }
///
/// // List available checkpoints for a processor
/// var checkpoints = await blobStore.ListCheckpointsAsync(processorId);
/// </code>
/// </example>
[PublicAPI]
public interface IBlobStore : IAsyncDisposable {
    /// <summary>
    /// Stores a blob with the specified key and optional metadata.
    /// </summary>
    /// <param name="key">The unique key identifying this blob.</param>
    /// <param name="data">The blob data to store.</param>
    /// <param name="metadata">Optional metadata describing the blob.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask StoreAsync(BlobKey key, ReadOnlyMemory<byte> data, BlobMetadata? metadata = null, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a blob by its key.
    /// </summary>
    /// <param name="key">The key of the blob to retrieve.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The blob data if found; otherwise, <c>null</c>.</returns>
    ValueTask<ReadOnlyMemory<byte>?> RetrieveAsync(BlobKey key, CancellationToken ct = default);

    /// <summary>
    /// Retrieves metadata for a blob without downloading its content.
    /// </summary>
    /// <param name="key">The key of the blob.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>The blob metadata if found; otherwise, <c>null</c>.</returns>
    ValueTask<BlobMetadata?> GetMetadataAsync(BlobKey key, CancellationToken ct = default);

    /// <summary>
    /// Deletes a blob.
    /// </summary>
    /// <param name="key">The key of the blob to delete.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask DeleteAsync(BlobKey key, CancellationToken ct = default);

    /// <summary>
    /// Checks whether a blob exists.
    /// </summary>
    /// <param name="key">The key of the blob to check.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns><c>true</c> if the blob exists; otherwise, <c>false</c>.</returns>
    ValueTask<bool> ExistsAsync(BlobKey key, CancellationToken ct = default);

    /// <summary>
    /// Lists all checkpoint IDs for a processor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns checkpoint IDs in ascending order.
    /// </para>
    /// </remarks>
    /// <param name="processorId">The processor identifier.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>An array of checkpoint IDs for the processor.</returns>
    ValueTask<long[]> ListCheckpointsAsync(string processorId, CancellationToken ct = default);
}
