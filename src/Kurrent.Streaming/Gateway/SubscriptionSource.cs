using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Options;

namespace Kurrent.Streaming.Gateway;

sealed class SubscriptionSource : ClientModuleBase, ISubscription {
    readonly StreamsClient                   _streams;
    readonly ProcessorOptions                _options;
    readonly ChannelWriter<SubscribeMessage> _writer;

    internal SubscriptionSource(
        KurrentClient client,
        ProcessorOptions options,
        ChannelWriter<SubscribeMessage> writer
    ) : base(client) {
        _streams = client.Streams;
        _options = options;
        _writer  = writer;
    }

    public async Task Subscribe(
        RecordPosition startPosition,
        RecordFilter filter,
        bool skipDecoding,
        CancellationToken ct
    ) {
        try {
            var subscription = _streams.Subscribe(
                filter, startPosition, skipDecoding,
                ct: ct);

            await foreach (var msg in subscription.ConfigureAwait(false)) {
                if (msg.Value is Record record) {
                    var partition = PartitionCalculator.XXHash
                        .Calculate(record.Stream, _options.MaxParallelism);

                    record = record with { Position = record.Position with { Partition = Partition.From(partition) } };

                    await _writer.WriteAsync(record, ct).ConfigureAwait(false);
                }
                else {
                    await _writer.WriteAsync(msg, ct).ConfigureAwait(false);
                }
            }

            _writer.TryComplete();
        }
        catch (OperationCanceledException) {
            _writer.TryComplete();
        }
        catch (ChannelClosedException) {
            _writer.TryComplete();
        }
        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
            _writer.TryComplete();
        }
        catch (Exception ex) {
            _writer.TryComplete(ex);
        }
    }

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;
}
