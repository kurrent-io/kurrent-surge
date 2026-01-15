namespace Kurrent.Client.Streams.Processing;

/// <summary>
/// Framework-internal interface extending <see cref="IStateStore"/> with checkpoint operations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Framework Use Only:</b> This interface is not intended for user code.
/// Users should interact with the <see cref="IStateStore"/> interface.
/// </para>
/// <para>
/// Provides checkpoint lifecycle operations for the processor framework:
/// <list type="bullet">
/// <item><see cref="SnapshotAsync(CancellationToken)"/> - Create state snapshots as compressed Parquet</item>
/// <item><see cref="RestoreAsync(ReadOnlyMemory{byte},CancellationToken)"/> - Restore state from Parquet snapshots</item>
/// <item><see cref="ClearPartitionsAsync(int[],CancellationToken)"/> - Clear state for lost partitions</item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Framework usage during checkpoint
/// var checkpointStore = (ICheckpointStateStore)stateStore;
///
/// // Snapshot assigned partitions
/// var parquetBytes = await checkpointStore.SnapshotAsync(assignedPartitions);
/// await blobStore.StoreAsync(new BlobKey(processorId, checkpointId), parquetBytes);
///
/// // Restore on restart
/// var downloaded = await blobStore.RetrieveAsync(blobKey);
/// if (downloaded.HasValue)
///     await checkpointStore.RestoreAsync(downloaded.Value, partitions);
///
/// // Clear on partition loss
/// await checkpointStore.ClearPartitionsAsync(lostPartitions);
/// </code>
/// </example>
[PublicAPI]
public interface ICheckpointStateStore : IStateStore, IAsyncDisposable {
    // ═══════════════════════════════════════════════════════════════════════════════
    // Snapshot operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a snapshot of all state as compressed Parquet bytes.
    /// </summary>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Compressed Parquet bytes representing the state snapshot.</returns>
    ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a snapshot of state for specified partitions.
    /// </summary>
    /// <param name="partitions">The partition numbers to include in the snapshot.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Compressed Parquet bytes representing the state snapshot.</returns>
    ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(int[] partitions, CancellationToken ct = default);

    /// <summary>
    /// Creates a snapshot of state for specified partition ranges.
    /// </summary>
    /// <param name="ranges">The partition ranges to include in the snapshot.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    /// <returns>Compressed Parquet bytes representing the state snapshot.</returns>
    ValueTask<ReadOnlyMemory<byte>> SnapshotAsync(PartitionRange[] ranges, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Restore operations
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Restores all state from compressed Parquet bytes.
    /// </summary>
    /// <param name="data">The compressed Parquet bytes from a previous snapshot.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RestoreAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Restores state for specified partitions from compressed Parquet bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only state for the specified partitions is restored from the snapshot.
    /// Other partitions in the snapshot data are ignored.
    /// </para>
    /// </remarks>
    /// <param name="data">The compressed Parquet bytes from a previous snapshot.</param>
    /// <param name="partitions">The partition numbers to restore.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RestoreAsync(ReadOnlyMemory<byte> data, int[] partitions, CancellationToken ct = default);

    /// <summary>
    /// Restores state for specified partition ranges from compressed Parquet bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only state for the specified partition ranges is restored from the snapshot.
    /// Other partitions in the snapshot data are ignored.
    /// </para>
    /// </remarks>
    /// <param name="data">The compressed Parquet bytes from a previous snapshot.</param>
    /// <param name="ranges">The partition ranges to restore.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask RestoreAsync(ReadOnlyMemory<byte> data, PartitionRange[] ranges, CancellationToken ct = default);

    // ═══════════════════════════════════════════════════════════════════════════════
    // Partition management
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clears state for specified partitions.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called during rebalancing when partitions are lost to another worker.
    /// Removes all state entries for the specified partitions.
    /// </para>
    /// </remarks>
    /// <param name="partitions">The partition numbers to clear.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask ClearPartitionsAsync(int[] partitions, CancellationToken ct = default);

    /// <summary>
    /// Clears state for specified partition ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called during rebalancing when partitions are lost to another worker.
    /// Removes all state entries for the specified partition ranges.
    /// </para>
    /// </remarks>
    /// <param name="ranges">The partition ranges to clear.</param>
    /// <param name="ct">Cancellation token for the operation.</param>
    ValueTask ClearPartitionsAsync(PartitionRange[] ranges, CancellationToken ct = default);
}
