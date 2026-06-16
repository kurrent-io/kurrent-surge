using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Processors.Configuration;
using Kurrent.Streaming.Options;
using Polly;

namespace Kurrent.Streaming.Gateway;

/// <summary>
/// The resilient source of <see cref="SubscribeMessage"/> for a drone. Wraps
/// <see cref="StreamsClient.Subscribe"/> in the processor's resilience pipeline so transient
/// errors reconnect from the last observed position and the drone sees a seamless stream with
/// no gaps. Tags each record with its partition and writes everything to the drone's channel.
/// </summary>
/// <remarks>
/// <see cref="StreamsClient.Subscribe"/> surfaces transient failures in two shapes: the connect
/// phase translates server errors into typed <see cref="Kurrent.Client.Exceptions.KurrentException"/>s
/// (generic status codes stay raw <see cref="RpcException"/>s), while the streaming phase
/// propagates raw <see cref="RpcException"/>s untranslated. The default retry predicate
/// (<see cref="ProcessorResilienceStrategy.DefaultRetry"/>) handles both shapes. Terminal domain
/// errors (stream deleted, position not found) never match it, so they propagate as-is and fault
/// the channel.
/// </remarks>
sealed class SubscriptionSource : ClientModuleBase, ISubscription {
    readonly StreamsClient                   _streams;
    readonly ProcessorOptions                _options;
    readonly ChannelWriter<SubscribeMessage> _writer;
    readonly ResiliencePipeline              _pipeline;

    internal SubscriptionSource(
        KurrentClient client,
        ProcessorOptions options,
        ChannelWriter<SubscribeMessage> writer
    ) : base(client) {
        _streams  = client.Streams;
        _options  = options;
        _writer   = writer;
        _pipeline = options.BuildResiliencePipeline(client.Options.LoggerFactory);
    }

    public async Task Subscribe(
        RecordPosition startPosition,
        RecordFilter filter,
        bool skipDecoding,
        CancellationToken ct
    ) {
        // Cursor advances as records/checkpoints flow and survives across retries: Polly
        // re-invokes the callback, which reconnects from the advanced position. Resume is
        // exclusive, so a transparent reconnect produces no duplicate and no gap.
        var cursor = startPosition;

        // The processor logger travels in the resilience context so both the SDK's default
        // OnRetry and any user strategy can log reconnection attempts.
        var context = ResilienceContextPool.Shared.Get(ct);
        context.Properties.Set(ProcessorResilienceStrategy.LoggerKey, Logger);

        try {
            await _pipeline.ExecuteAsync(
                async ctx => {
                    var token = ctx.CancellationToken;

                    var options = new SubscribeOptions {
                        Filter       = filter,
                        Start        = cursor,
                        SkipDecoding = skipDecoding
                    };

                    await foreach (var msg in _streams.Subscribe(options, token).ConfigureAwait(false)) {
                        // Tag each record with its partition before handing it downstream; pass
                        // everything else (Connected, Checkpoint) through untouched.
                        if (msg.Value is Record record) {
                            var partition = PartitionCalculator.XXHash
                                .Calculate(record.Stream, _options.MaxParallelism);

                            record = record with { Position = record.Position with { Partition = Partition.From(partition) } };

                            await _writer.WriteAsync(record, token).ConfigureAwait(false);
                        } else
                            await _writer.WriteAsync(msg, token).ConfigureAwait(false);

                        // Hand off first, then advance: if a later MoveNext faults we reconnect
                        // past the message already delivered.
                        cursor = Advance(cursor, msg);
                    }
                }, context).ConfigureAwait(false);

            _writer.TryComplete();
        } catch (OperationCanceledException) {
            _writer.TryComplete();
        } catch (ChannelClosedException) {
            _writer.TryComplete();
        } catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
            _writer.TryComplete();
        } catch (Exception ex) {
            _writer.TryComplete(ex);
        } finally {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Advances the resume cursor from an observed message. Records carry a full position; server
    /// checkpoints (<see cref="FilterCheckpoint"/>s and <see cref="CaughtUpCheckpoint"/>s) carry the
    /// log scan position — the client resolves a stream CaughtUp's log position up front — letting us
    /// skip empty regions on reconnect.
    /// </summary>
    static RecordPosition Advance(RecordPosition cursor, SubscribeMessage msg) =>
        msg.Value switch {
            Record record => record.Position,
            FilterCheckpoint   { Positions: [.., var w] } => w,
            CaughtUpCheckpoint { Positions: [.., var w] } => w,
            _ => cursor
        };
}

public interface ISubscription : IAsyncDisposable {
    Task Subscribe(
        RecordPosition startPosition,
        RecordFilter filter,
        bool skipDecoding,
        CancellationToken ct
    );
}