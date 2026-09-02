using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Checkpointing;
using Kurrent.Surge.Streaming.Models;

namespace Kurrent.Surge.Streaming.Tests.Fakes;

/// <summary>
/// A checkpoint store that always loads a fixed set of per-partition positions — the catch-up floor a
/// drone resumes from. Unlike <see cref="InMemoryCheckpointStore"/> (which resolves to Earliest/Latest),
/// this lets a test stand the drone up as if a prior run had already committed up to the seeded
/// positions, so the pre-dispatch catch-up prune has a real prefix to drop.
/// </summary>
sealed class SeededCheckpointStore(RecordPosition[] seed) : ICheckpointStore {
    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(string droneId, CancellationToken ct = default) =>
        new(new ProcessorCheckpoint { Positions = seed });

    public ValueTask<ProcessorCheckpoint> LoadLatestAsync(
        string droneId, Partition[] partitions,
        SubscriptionInitialPosition initialPosition, CancellationToken ct = default
    ) => new(new ProcessorCheckpoint { Positions = seed });

    public ValueTask StoreAsync(
        string droneId, string checkpointId,
        RecordPosition[] positions, CancellationToken ct = default
    ) => ValueTask.CompletedTask;

    public ValueTask<ProcessorCheckpoint> ResetAsync(
        string droneId, string checkpointId,
        LogPosition position, CancellationToken ct = default
    ) => new(ProcessorCheckpoint.Empty);
}
