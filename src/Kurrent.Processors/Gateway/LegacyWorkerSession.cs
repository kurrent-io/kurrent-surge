using System.Threading.Channels;
using Kurrent.Client;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Processors.Partitioning;

namespace Kurrent.Processors.Gateway;

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

    int _disposed;

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
        // Send work order assignment
        yield return CreateWorkOrder();

        // Block until worker signals ready
        if (!await _workerReadySignal.Task.WaitUnlessCancelledAsync(_cancellator.Token))
            yield break;

        await foreach (var msg in _channel.Reader.ReadAllAsync(ct)) {
            if (msg.IsRecord)
                yield return msg.AsRecord;
            else
                yield return msg.AsCheckpoint;
        }


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

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        // Cancel triggers cascade:
        // pump exits → completes channel → disposes gRPC
        await _cancellator
            .CancelAsync()
            .ConfigureAwait(false);

        _cancellator.Dispose();
    }
}
