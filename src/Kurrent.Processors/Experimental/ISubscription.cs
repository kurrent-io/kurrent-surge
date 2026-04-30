using Kurrent.Client.Streams;
using Kurrent.Processors.Gateway;

namespace Kurrent.Processors.Experimental;

/// <summary>
/// Source of <see cref="SubscribeMessage"/> for the experimental drone — the seam
/// where the real <see cref="Kurrent.Processors.Gateway.SubscriptionSource"/> would
/// plug in.
/// </summary>
public interface ISubscription {
    IAsyncEnumerable<SubscribeMessage> Subscribe(
        RecordPosition[]  startPositions,
        CancellationToken ct);
}
