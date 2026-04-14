using Kurrent.Client.Streams;

namespace Kurrent.Processors.Gateway;

public interface ISubscriptionSource : IAsyncDisposable {
    IAsyncEnumerable<SubscribeMessage> Subscribe(
        IRecordFilter filter,
        RecordPosition from,
        bool skipDecoding,
        CancellationToken ct);
}
