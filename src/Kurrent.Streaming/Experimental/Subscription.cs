using System.Runtime.CompilerServices;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Gateway;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Experimental;

/// <summary>
/// <see cref="ISubscription"/> backed by the V1 streams gRPC subscription. Modeled after
/// <see cref="SubscriptionSource"/> but reduced to the
/// subscription-shaped slice: open the gRPC stream and yield <see cref="SubscribeMessage"/>
/// — Connected/Record/Checkpoint. Partition tagging happens here (records arrive without
/// a partition; we hash them client-side via <see cref="PartitionCalculator"/>).
/// <para>
/// Position management, the interval timer, and the checkpoint store are NOT this
/// component's responsibility — they live on the experimental <see cref="Checkpointer"/>.
/// </para>
/// </summary>
public sealed class Subscription(KurrentClient client, ProcessorOptions options) : ClientModuleBase(client), ISubscription {
    readonly StreamsClient _streams = new(client);

    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        RecordPosition[]                              startPositions,
        [EnumeratorCancellation] CancellationToken    ct
    ) {
        // The V1 wire is a single subscription over the log; per-partition filtering is
        // calculated client-side. Start at the lowest position the checkpointer loaded —
        // matches the production source's LowWatermark behavior.
        var from = startPositions.Length > 0
            ? startPositions.Min()
            : RecordPosition.Earliest;

        var underlying = _streams.Subscribe(
            options.Subscription.Filter, from,
            options.SkipDecoding, ct: ct);

        var positionsIndex = startPositions
            .ToDictionary(x => x.Partition);

        
        await foreach (var msg in underlying.ConfigureAwait(false)) {
            if (msg.Value is Record record) {
                var partition = PartitionCalculator.XXHash
                    .Calculate(record.Stream, options.MaxParallelism);

                // the subscription doesn't do server-side filtering by partition
                // skip records already processed (after low watermark, before partition checkpoint position)
                
                if (positionsIndex.TryGetValue(partition, out var start) && record.Position <= start) 
                    continue;
                
                yield return record with {
                    Position = record.Position with { Partition = partition }
                };
            }
            else
                yield return msg;
        }
    }
}
