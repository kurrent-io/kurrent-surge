using System.Threading.Channels;
using Google.Rpc.Exceptions;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Options;
using Polly;
using Polly.Retry;

namespace Kurrent.Streaming.Gateway;

/// <summary>
/// The resilient source of <see cref="SubscribeMessage"/> for a drone. Wraps
/// <see cref="StreamsClient.Subscribe"/> in a Polly retry pipeline that reconnects on transient
/// gRPC errors, resuming from the last observed position so the drone sees a seamless stream with
/// no gaps. Tags each record with its partition and writes everything to the drone's channel.
/// </summary>
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
        _pipeline = BuildPipeline(options.Resilience);
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

        try {
            await _pipeline.ExecuteAsync(
                async token => {
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
                }, ct).ConfigureAwait(false);

            _writer.TryComplete();
        } catch (OperationCanceledException) {
            _writer.TryComplete();
        } catch (ChannelClosedException) {
            _writer.TryComplete();
        } catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
            _writer.TryComplete();
        } catch (CancelledException) {
            _writer.TryComplete();
        } catch (Exception ex) {
            _writer.TryComplete(ex);
        }
    }

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;

    #region ->> Resilience <<-

    /// <summary>
    /// Builds the reconnection pipeline once (immutable, reused across reconnects). Only transient
    /// gRPC status codes and <see cref="IOException"/> trigger a retry; everything else propagates.
    /// </summary>
    /// <remarks>
    /// <see cref="StreamsClient.Subscribe"/> surfaces transient failures as raw
    /// <see cref="RpcException"/> on both the connect path (its <c>AsWellKnownException</c>
    /// translation returns the original exception when no domain parser matches a generic status
    /// code) and the streaming path (<c>MoveNextSafe</c> only swallows cancellation). Terminal
    /// domain errors (stream deleted, position not found) are already translated and never match
    /// the predicate, so they propagate as-is and fault the channel.
    /// </remarks>
    ResiliencePipeline BuildPipeline(ResilienceOptions options) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(rex => options.RetryableStatusCodes.Contains(rex.StatusCode))
                    .Handle<IOException>(),
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay            = options.BaseDelay,
                MaxDelay         = options.MaxDelay,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry = args => {
                    Logger.LogWarning(
                        args.Outcome.Exception,
                        "Subscription reconnecting in {Delay} (attempt {Attempt})",
                        args.RetryDelay, args.AttemptNumber + 1);

                    return default;
                }
            })
            .Build();

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

    #endregion
}
