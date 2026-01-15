using System.Collections.Immutable;
using Kurrent.Client.Streams;

namespace Kurrent.Client.Processors.Checkpointing;

/// <summary>
/// Stores and retrieves partition checkpoints for a processor group.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="WorkerCheckpointStore"/> which maintains per-worker streams,
/// this store uses a single shared stream per processor where all workers
/// contribute their partition checkpoints.
/// </para>
/// <para>
/// Loading returns all partition checkpoints regardless of which worker stored them.
/// Storing merges new partition positions with existing ones, tracking which worker
/// last updated each partition.
/// </para>
/// </remarks>
[PublicAPI]
public interface IProcessorCheckpointStore {
    /// <summary>
    /// Loads the checkpoint for the specified processor.
    /// </summary>
    /// <param name="processorId">The processor group identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The checkpoint containing all partition checkpoints, barrier ID, and resolution logic.
    /// Returns <see cref="ProcessorCheckpoint.Empty"/> if no checkpoint exists.
    /// </returns>
    ValueTask<ProcessorCheckpoint> LoadAsync(
        string processorId,
        CancellationToken ct = default);

    /// <summary>
    /// Stores checkpoint positions for the specified partitions, merging with existing positions.
    /// </summary>
    /// <param name="processorId">The processor group identifier.</param>
    /// <param name="workerId">The worker storing the checkpoint (for attribution).</param>
    /// <param name="barrierId">The barrier that triggered this checkpoint.</param>
    /// <param name="positions">Partition positions to checkpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when positions dictionary is empty.</exception>
    /// <remarks>
    /// <para>
    /// Internally loads the current checkpoint state and merges the provided positions
    /// with existing ones. Uses optimistic concurrency with automatic retry on conflicts.
    /// </para>
    /// <para>
    /// Only the specified partitions are updated; other partitions retain their positions.
    /// </para>
    /// </remarks>
    ValueTask StoreAsync(
        string processorId,
        string workerId,
        LogPosition barrierId,
        ImmutableDictionary<Partition, LogPosition> positions,
        CancellationToken ct = default);
}
