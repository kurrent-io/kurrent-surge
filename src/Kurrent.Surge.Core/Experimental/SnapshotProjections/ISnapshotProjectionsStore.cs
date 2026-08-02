using Kurrent.Surge.Client.Streams;

namespace Kurrent.SnapshotProjections;

/// <summary>
/// Loads and saves the snapshot maintained by a <see cref="SnapshotProjectionsModule{TSnapshot}"/>.
/// A snapshot stream holds one record per snapshot version, so the last record is the current state.
/// </summary>
interface ISnapshotProjectionsStore {
    /// <summary>
    /// Reads the current snapshot from <paramref name="snapshotStream"/>. Yields a fresh
    /// <typeparamref name="TSnapshot"/> at <see cref="RecordPosition.Unset"/> when the stream holds nothing
    /// that decodes to <typeparamref name="TSnapshot"/>.
    /// </summary>
    ValueTask<(TSnapshot Snapshot, RecordPosition Position, DateTimeOffset Timestamp)> LoadSnapshot<TSnapshot>(
        StreamName snapshotStream,
        CancellationToken cancellationToken = default
    ) where TSnapshot : class, new();

    /// <summary>
    /// Appends <paramref name="snapshot"/> to <paramref name="snapshotStream"/>, guarding on
    /// <paramref name="expectedState"/> so a concurrent projector cannot overwrite an unseen revision.
    /// </summary>
    ValueTask SaveSnapshot<TSnapshot>(
        TSnapshot snapshot,
        StreamName snapshotStream,
        ExpectedStreamState expectedState,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken = default
    ) where TSnapshot : class, new();
}