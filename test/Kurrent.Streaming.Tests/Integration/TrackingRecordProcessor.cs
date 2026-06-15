using System.Collections.Concurrent;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Streaming.Tests;

sealed class TestRecordBatchProcessor : RecordBatchProcessor {
    readonly ConcurrentQueue<RecordBatch>       _batches     = new();
    readonly ConcurrentQueue<CheckpointContext> _checkpoints = new();

    public Func<BatchContext, ValueTask>?      OnProcessBatch   { get; set; }
    public Func<ConnectedContext, ValueTask>?  OnConnectedHook  { get; set; }
    public Func<CheckpointContext, ValueTask>? OnCheckpointHook { get; set; }

    public ConnectedContext? Connected { get; private set; }

    public RecordBatch[]                    Batches     => _batches.ToArray();
    public IReadOnlyList<Record>            Records     => _batches.SelectMany(b => b).ToList();
    public IReadOnlyList<CheckpointContext> Checkpoints => _checkpoints.ToArray();

    public override async ValueTask ProcessBatch(BatchContext context) {
        _batches.Enqueue(context.Records);

        if (OnProcessBatch is not null)
            await OnProcessBatch(context);
    }

    public override async ValueTask OnConnected(ConnectedContext context) {
        Connected = context;

        if (OnConnectedHook is not null)
            await OnConnectedHook(context);
    }

    public override async ValueTask OnCheckpoint(CheckpointContext context) {
        _checkpoints.Enqueue(context);

        if (OnCheckpointHook is not null)
            await OnCheckpointHook(context);
    }
}