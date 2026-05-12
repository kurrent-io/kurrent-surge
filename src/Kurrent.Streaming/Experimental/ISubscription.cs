using Kurrent.Client.Streams;
using Kurrent.Streaming.Gateway;

namespace Kurrent.Streaming.Experimental;

/// <summary>
/// Source of <see cref="SubscribeMessage"/> for the experimental drone — the seam
/// where the real <see cref="SubscriptionSource"/> would
/// plug in.
/// </summary>
public interface ISubscription {
    IAsyncEnumerable<SubscribeMessage> Subscribe(
        RecordPosition[]  startPositions,
        CancellationToken ct);
}
