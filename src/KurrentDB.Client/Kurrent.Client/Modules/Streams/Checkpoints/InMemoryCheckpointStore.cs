// namespace Kurrent.Client.Streams.Checkpoints;
//
// public class InMemoryCheckpointStore(string consumerId) : ICheckpointStore {
//     InMemoryStateStore Cache { get; } = new InMemoryStateStore();
//
//     public async Task<IReadOnlyList<RecordPosition>> LoadPositions(CancellationToken cancellationToken = default) {
//         var positions = await Cache.Get<RecordPosition[]>(consumerId, cancellationToken);
//         return positions ?? [];
//     }
//
//     public async Task<IReadOnlyList<RecordPosition>> CommitPositions(IReadOnlyList<RecordPosition> positions, CancellationToken cancellationToken = default) {
//         await Cache.Set(consumerId, positions, cancellationToken);
//         return positions;
//     }
// }
