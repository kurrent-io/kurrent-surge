// using Google.Protobuf.WellKnownTypes;
// using KurrentDB.Protocol.Streams.V2;
//
// namespace Kurrent.Client.Streams.Checkpoints;
//
// /// <summary>
// /// Represents a position checkpoint store. This implementation will only store the last committed position.
// /// </summary>
// public class CheckpointStore(string consumerId, KurrentClient client, TimeProvider time, StreamName stream) : ICheckpointStore {
//     static readonly SemaphoreSlim Locker = new(1, 1);
//
//     string        ConsumerId       { get; } = consumerId;
//     TimeProvider  Time             { get; } = time;
//     KurrentClient Client           { get; } = client;
//     StreamName    CheckpointStream { get; } = stream == StreamName.None ? $"$consumer/{consumerId}/checkpoint" : stream;
//
//     StreamRevision ExpectedRevision { get; set; } = StreamRevision.Unset;
//
//     public async Task<IReadOnlyList<RecordPosition>> LoadPositions(CancellationToken cancellationToken = default) {
//         var record = await Client.Streams.ReadLastStreamRecord(CheckpointStream, cancellationToken).ThrowOnFailureAsync();
//
//         switch (record.Value) {
//             case Checkpoint checkpoint:
//                 ExpectedRevision = record.StreamRevision;
//                 return [RecordPosition.From(LogPosition.From((long)checkpoint.LogPosition))];
//
//             default:
//                 ExpectedRevision = StreamRevision.Unset;
//                 return [];
//         }
//     }
//
//     public async Task<IReadOnlyList<RecordPosition>> CommitPositions(IReadOnlyList<RecordPosition> positions, CancellationToken cancellationToken = default) {
//         if (positions.Count == 0 || positions.Any(x => x == RecordPosition.Unset))
//             throw new ArgumentException("Cannot commit unset positions.");
//
//         try {
//             await Locker.WaitAsync(CancellationToken.None);
//
//             var lastPosition = positions[^1];
//
//             StreamRevision expectedRevision = ExpectedRevision == StreamRevision.Unset
//                 ? ExpectedStreamState.NoStream
//                 : ExpectedRevision;
//
//             var request = AppendStreamRequest.New()
//                 .ForStream(CheckpointStream)
//                 .ExpectingRevision(expectedRevision)
//                 .WithMessage(MapToCheckpoint(ConsumerId, lastPosition, Time.GetUtcNow()))
//                 .Build();
//
//             var result = await Client.Streams.Append(request, cancellationToken)
//                 .ThrowOnFailureAsync()
//                 .ConfigureAwait(false);
//
//             ExpectedRevision = result.StreamRevision;
//
//             return positions;
//         } finally{
//             Locker.Release();
//         }
//
//         static Checkpoint MapToCheckpoint(string consumerId, RecordPosition position, DateTimeOffset timestamp) =>
//             new() {
//                 ConsumerId     = consumerId,
//                 LogPosition    = position.LogPosition,
//                 Timestamp      = timestamp.ToTimestamp()
//             };
//     }
// }
