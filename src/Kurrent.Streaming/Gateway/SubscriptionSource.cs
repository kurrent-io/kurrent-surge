using System.Threading.Channels;
using Google.Rpc.Exceptions;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Options;
using Polly;

namespace Kurrent.Streaming.Gateway;

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
        _pipeline = ResilientSubscription.BuildPipeline(options.Resilience, Logger);
    }

    public async Task Subscribe(
        RecordPosition startPosition,
        RecordFilter filter,
        bool skipDecoding,
        CancellationToken ct
    ) {
        try {
            await ResilientSubscription
                .PumpAsync(_streams, _pipeline, filter, startPosition, skipDecoding, WriteAsync, ct)
                .ConfigureAwait(false);

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

        // Tag each record with its partition before handing it downstream; pass everything
        // else (Connected, Checkpoint) through untouched. Invoked once per message by the
        // resilient pump, which reconnects transparently on transient errors.
        async ValueTask WriteAsync(SubscribeMessage msg, CancellationToken token) {
            if (msg.Value is Record record) {
                var partition = PartitionCalculator.XXHash
                    .Calculate(record.Stream, _options.MaxParallelism);

                record = record with { Position = record.Position with { Partition = Partition.From(partition) } };

                await _writer.WriteAsync(record, token).ConfigureAwait(false);
            } else
                await _writer.WriteAsync(msg, token).ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;
}
