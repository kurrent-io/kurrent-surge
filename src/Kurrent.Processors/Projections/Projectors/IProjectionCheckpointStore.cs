// ORIGINAL SURGE IMPLEMENTATION — preserved for reference during refactor to IRecordProcessor.
// Disabled from compilation; re-enable by removing the #if false / #endif wrapping below.
#if false

using Kurrent.Client.Streams;

namespace Kurrent.Surge.Projectors;

public interface IProjectionCheckpointStore {
    ValueTask                 EnsureStoreExists(CancellationToken ct);
    ValueTask<RecordPosition> LoadCheckpoint(CancellationToken ct);
    ValueTask<RecordPosition> StoreCheckpoint(RecordPosition position, CancellationToken ct);
    ValueTask<RecordPosition> ResetCheckpoint(CancellationToken ct);
}

public class ProjectionCheckpointStoreException(string message, Exception? innerException = null) : Exception(message, innerException);

public class FailedToCreateCheckpointsTableException(string key, Exception? innerException = null)
    : ProjectionCheckpointStoreException($"Failed to create checkpoints table for '{key}'", innerException);

public class FailedToLoadCheckpointException(string key, Exception? innerException = null)
    : ProjectionCheckpointStoreException($"Failed to load checkpoint for '{key}'", innerException);

public class FailedToStoreCheckpointException(string key, RecordPosition position, Exception? innerException = null)
    : ProjectionCheckpointStoreException($"Failed to store checkpoint {position} for '{key}'", innerException);

public class FailedToResetCheckpointException(string key, Exception? innerException = null)
    : ProjectionCheckpointStoreException($"Failed to reset checkpoint for '{key}'", innerException);

public static class ProjectionCheckpointStoreExtensions {
    public static async ValueTask<RecordPosition[]> CommitPositions(this IProjectionCheckpointStore store, RecordPosition[] positions, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        if (positions.Length > 0)
            await store
                .StoreCheckpoint(positions[^1], ct)
                .ConfigureAwait(false);

        return positions;
    }
}

#endif
