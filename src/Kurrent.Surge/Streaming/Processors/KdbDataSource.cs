using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Surge.Client.Processors.Partitioning;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Streaming.Options;
using Polly;

namespace Kurrent.Surge.Streaming.Gateway;


public record KdbDataSourceOptions {
    public RecordFilter                Filter               { get; init; } = RecordFilter.None;
    public bool                        SkipDecoding         { get; init; } = false;
    public bool                        ResolveLinks         { get; init; } = true;
    public uint                        MaxSearchWindow      { get; init; } = StreamDefaults.DefaultMaxSearchWindow;
    public string                      PartitionKeySelector { get; set; }  = "";
    public int                         MaxParallelism       { get; set; }  = 1;
    public ResiliencePipeline          ResiliencePipeline   { get; init; } = null!;
}

public class KdbDataSource : IDataSource {
    readonly KdbDataSourceOptions   _options;
    readonly IStreamsClient         _client;
    readonly ILoggerFactory         _loggerFactory;
    readonly IPartitionKeyExtractor _partitionKeyExtractor;

    public KdbDataSource(
        KdbDataSourceOptions options,
        IStreamsClient client,
        ILoggerFactory loggerFactory
    ) {
        _options               = options;
        _client                = client;
        _loggerFactory         = loggerFactory;
        _partitionKeyExtractor = PartitionKeyExtractor.Create(_options.PartitionKeySelector);
    }

    public async Task Connect(ChannelWriter<DataSourceMessage> writer, RecordPosition[] startPositions, CancellationToken ct) {
        // Feed geometry, derived from the checkpointer's resolved per-partition start:
        //   * cursor / low watermark — the least-advanced partition, where the single shared scan subscribes
        //     and re-subscribes across retries (Polly re-invokes the callback from the advanced cursor; resume
        //     is exclusive, so a transparent reconnect produces no duplicate and no gap).
        //   * per-partition floors — real committed positions the caught-up prune compares against; the
        //     Latest/Unset sentinels (partitions with no prior progress) are excluded, they have nothing to skip.
        //   * high watermark — the most-advanced floor; once the scan clears it the prune latches off, since the
        //     subscription is delivered in log order.
        var cursor = startPositions.Length > 0 ? startPositions.Min() : RecordPosition.Earliest;

        var partitionPositions = startPositions
            .Where(p => p.LogPosition != LogPosition.Latest && p.LogPosition != LogPosition.Unset)
            .ToDictionary(p => p.Partition);

        var highWatermark = partitionPositions.Count > 0 ? partitionPositions.Values.Max() : RecordPosition.Earliest;
        var caughtUp      = partitionPositions.Count == 0;

        var defaultOptions = new SubscribeOptions {
            Filter          = _options.Filter,
            SkipDecoding    = _options.SkipDecoding,
            ResolveLinks    = _options.ResolveLinks,
            MaxSearchWindow = _options.MaxSearchWindow
        };

        var context = ResilienceContextPool.Shared.Get(ct);

        context.Logger = _loggerFactory.CreateLogger<KdbDataSource>();

        try {
            await _options.ResiliencePipeline.ExecuteAsync(async ctx => {
                await foreach (var msg in _client.Subscribe(defaultOptions with { Start = cursor }, ctx.CancellationToken)) {
                    switch (msg.Value) {
                        case Connected: {
                            // Forward the subscription-established marker so the drone fires OnConnected. The
                            // client yields it at the head of the stream; the resilience pipeline re-subscribes
                            // on transient failures, each reconnect emitting a fresh Connected with a new
                            // SessionId, so forwarding every one keeps DroneContext.SessionId current.
                            // BitCast reinterprets the whole SubscribeMessage as the layout-identical
                            // DataSourceMessage, reusing the boxed Connected (zero-alloc) — see
                            // VariantLayoutCompatibilityTests, which guards this reinterpret.
                            await writer.WriteAsync(Unsafe.BitCast<SubscribeMessage, DataSourceMessage>(msg), ctx.CancellationToken);
                            break;
                        }

                        case Record rec: {
                            var assigned = AssignPartition(rec);
                            var position = assigned.Position;

                            // Skip already-processed records: advance the cursor past them, don't redeliver.
                            // RecordPosition compares by log position, so these are plain log-order checks.
                            if (!caughtUp) {
                                if (position > highWatermark)
                                    caughtUp = true;
                                else if (partitionPositions.TryGetValue(position.Partition, out var processed) && position <= processed) {
                                    cursor = position;
                                    break;
                                }
                            }

                            await writer.WriteAsync(assigned, ctx.CancellationToken);
                            cursor = position;
                            break;
                        }

                        case Checkpoint cpt: {
                            await writer.WriteAsync(cpt, ctx.CancellationToken);
                            cursor = cpt.Positions[^1];
                            break;
                        }
                    }
                }
            }, context);

            writer.TryComplete();
        } catch (OperationCanceledException) {
            writer.TryComplete();
        } catch (ChannelClosedException) {
            writer.TryComplete();
        } catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
            writer.TryComplete();
        } catch (Exception ex) {
            writer.TryComplete(ex);
        } finally {
            ResilienceContextPool.Shared.Return(context);
        }

        Record AssignPartition(Record record) =>
            record with { Position = record.Position with { Partition = CalculatePartition(record) } };

        int CalculatePartition(Record record) =>
            PartitionCalculator.XXHash.Calculate(_partitionKeyExtractor.Extract(record) ?? record.Stream, _options.MaxParallelism);
    }
}
