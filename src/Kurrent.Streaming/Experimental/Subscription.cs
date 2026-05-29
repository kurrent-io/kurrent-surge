using System.Runtime.CompilerServices;
using System.Threading.Channels;
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
        RecordPosition[]                           startPositions,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        // The V1 wire is a single subscription over the log; per-partition filtering is
        // calculated client-side. Start at the lowest position the checkpointer loaded —
        // matches the production source's LowWatermark behavior.
        var from = startPositions.Length > 0
            ? startPositions.Min()
            : RecordPosition.Earliest;

        var positionsIndex = startPositions.ToDictionary(x => x.Partition);

        var pipeline = ResilientSubscription.BuildPipeline(options.Resilience, Logger);

        // Polly's pipeline cannot yield, so bridge the resilient pump to this IAsyncEnumerable
        // through a bounded channel. The channel-writing sources (SubscriptionSource, the Hive
        // master) feed the pump directly and need no such hop — only this enumerable-shaped path
        // does.
        var channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(options.BufferCapacity) {
                SingleWriter = true,
                SingleReader = true,
                FullMode     = BoundedChannelFullMode.Wait
            });

        // Linked so abandoning enumeration (consumer breaks early) cancels the pump instead of
        // leaving it blocked on a full channel.
        using var pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var pump = RunPump();

        try {
            await foreach (var msg in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
                yield return msg;
        }
        finally {
            await pumpCts.CancelAsync().ConfigureAwait(false);
            await pump.ConfigureAwait(false);
        }

        yield break;

        async Task RunPump() {
            try {
                await ResilientSubscription
                    .PumpAsync(_streams, pipeline, options.Subscription.Filter, from, options.SkipDecoding, Forward, pumpCts.Token)
                    .ConfigureAwait(false);

                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) {
                channel.Writer.TryComplete();
            }
            catch (Exception ex) {
                channel.Writer.TryComplete(ex);
            }
        }

        async ValueTask Forward(SubscribeMessage msg, CancellationToken token) {
            if (msg.Value is Record record) {
                var partition = PartitionCalculator.XXHash
                    .Calculate(record.Stream, options.MaxParallelism);

                // The subscription doesn't do server-side filtering by partition: skip records
                // already processed (after low watermark, before this partition's checkpoint).
                if (positionsIndex.TryGetValue(partition, out var start) && record.Position <= start)
                    return;

                await channel.Writer
                    .WriteAsync(record with { Position = record.Position with { Partition = partition } }, token)
                    .ConfigureAwait(false);
            }
            else
                await channel.Writer.WriteAsync(msg, token).ConfigureAwait(false);
        }
    }
}
