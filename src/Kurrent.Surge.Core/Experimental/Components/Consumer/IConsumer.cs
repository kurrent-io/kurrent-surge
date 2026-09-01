using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Experimental;

interface IConsumer : IAsyncDisposable {
    IAsyncEnumerable<Record> Consume(ConsumerOptions options, CancellationToken ct = default);
}

sealed class ConsumerOptions : SubscribeOptions;

// interface IConsumerCheckpointController : IAsyncDisposable {
//     // public ValueTask<RecordPosition[]> Track(Record record, CancellationToken ct = default);
//     //    
//     // public ValueTask<RecordPosition[]> CommitAll(CancellationToken ct = default);
//     //
//     // public ValueTask<RecordPosition[]> GetLatestPositions(CancellationToken ct = default);
// }