using System.Threading.Channels;
using Kurrent.Client.Processors.Gateway;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Processors.Partitioning;
using static System.TimeSpan;

namespace Kurrent.Client.Streams;

/// <summary>
/// Legacy implementation of worker session using V1 protocol.
/// Wraps a channel for batched message delivery and delegates outbound messages to the gateway.
/// </summary>
sealed class LegacyWorkerSession : IWorkerSession {
    readonly CancellationTokenSource        _cancellator;
    readonly Channel<LegacyGatewayMessage>  _channel;
    readonly Func<WorkerMessage, ValueTask> _sendHandler;
    readonly ProcessorSettings              _settings;
    readonly TaskCompletionSource           _workerReadySignal;


    internal LegacyWorkerSession(
        SessionId sessionId,
        Channel<LegacyGatewayMessage> channel,
        ProcessorSettings settings,
        Func<WorkerMessage, ValueTask> sendHandler,
        CancellationTokenSource cancellator
    ) {
        SessionId          = sessionId;
        _channel           = channel;
        _settings          = settings;
        _sendHandler       = sendHandler;
        _cancellator       = cancellator;
        _workerReadySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public SessionId SessionId { get; }

    public ValueTask SendAsync(WorkerMessage message) {
        // Unblock enumerator when worker signals ready
        if (message.IsWorkerReady)
            _workerReadySignal.TrySetResult();

        return _sendHandler(message);
    }

    public async IAsyncEnumerator<ProcessorControlMessage> GetAsyncEnumerator(CancellationToken ct = default) {
        var messages = _channel.Reader.ReadAllAsync(ct).ReadBatched(
            _settings.Batching.MaxSize, FromMilliseconds(_settings.Batching.Timeout),
            static msg => msg.IsRecord
                ? BatchAction<Record, ProcessorControlMessage>.Batch(msg.AsRecord)
                : BatchAction<Record, ProcessorControlMessage>.FlushThenYield(msg.AsCheckpointBarrier),
            static records => RecordBatch.From([..records]),
            ct
        );

        try {
            // Send work order assignment
            yield return CreateWorkOrder();

            // Block until worker signals ready
            await _workerReadySignal.Task.ConfigureAwait(false);

            await foreach (var message in messages.ConfigureAwait(false))
                yield return message;
        }
        finally {
            await StopSessionAsync().ConfigureAwait(false);
        }

        yield break;

        WorkOrder CreateWorkOrder() {
            // Use PartitionAssigner to simulate server behavior
            // When multiple workers join, each gets a subset of partitions
            var manifest = PartitionAssigner.Default.Distribute(
                DistributionManifest.Empty,
                [_settings.WorkerId],
                _settings.MaxParallelism
            );

            return new WorkOrder([..manifest[_settings.WorkerId].All]);
        }
    }

    public ValueTask DisposeAsync() => StopSessionAsync();

    async ValueTask StopSessionAsync() {
        if (!_cancellator.IsCancellationRequested) return;
        await _cancellator.CancelAsync().ConfigureAwait(false);
        _cancellator.Dispose();
    }
}
