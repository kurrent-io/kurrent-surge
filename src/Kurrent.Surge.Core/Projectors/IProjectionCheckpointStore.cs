using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Projectors.Transactional;

/// <summary>
/// Reads and writes a projection's resume positions in the projection's own storage. Stateless: the
/// client is passed per call, so the store never holds a connection or a transaction of its own.
/// </summary>
/// <remarks>
/// <para>
/// Whether a call lands inside a transaction is decided entirely by which client the caller hands over.
/// <see cref="Store"/> is always given the client of the projection's open transaction, so positions
/// commit with the projected rows they describe. <see cref="Load"/> and <see cref="EnsureStoreExists"/>
/// have no such requirement.
/// </para>
/// <para>
/// The store keeps the resume floor: the positions it last read or wrote. Every write is guarded on
/// that floor, so the store needs nothing from the caller to detect a foreign writer. An instance lives
/// exactly one run — a restart constructs a fresh store, and <see cref="Load"/> reads the floor from
/// the durable rows.
/// </para>
/// <para>
/// Positions are per partition. A single scalar cannot distinguish "this partition had no records" from
/// "this partition's records are unconfirmed", which is why every method deals in arrays.
/// </para>
/// </remarks>
/// <typeparam name="TClient">The backend's client type — a connection, or whatever it writes through.</typeparam>
[PublicAPI]
public interface IProjectionCheckpointStore<in TClient> {
    /// <summary>Creates the checkpoint storage if it does not exist. Must be idempotent.</summary>
    ValueTask EnsureStoreExists(TClient client, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the resume position for every partition in <paramref name="partitions"/>, falling back to
    /// <paramref name="initialPosition"/> for any partition with nothing stored.
    /// </summary>
    ValueTask<RecordPosition[]> Load(
        TClient client,
        Partition[] partitions,
        SubscriptionInitialPosition initialPosition,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Advances the stored positions to <paramref name="next"/>.
    /// </summary>
    /// <remarks>
    /// A guarded write: each row moves only if it still holds the position this store last read or
    /// wrote for it — the floor. A row that does not match means something else advanced it — the
    /// implementation throws, which rolls back the transaction and everything the projection wrote
    /// into it. That single condition covers both concurrency and monotonicity; no separate lock or
    /// pre-check is needed.
    /// </remarks>
    ValueTask Store(
        TClient client,
        RecordPosition[] next,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Rewinds every partition to <paramref name="position"/>, discarding whatever was stored. Called
    /// only when an explicit start position overrides the projection's own progress.
    /// </summary>
    /// <remarks>
    /// This rewinds <b>positions</b>. Whether the projected data must also be discarded is the
    /// projection's decision, not the store's — only the projection knows whether its writes are
    /// idempotent under replay.
    /// </remarks>
    ValueTask Reset(
        TClient client,
        LogPosition position,
        Partition[] partitions,
        CancellationToken cancellationToken
    );
}
