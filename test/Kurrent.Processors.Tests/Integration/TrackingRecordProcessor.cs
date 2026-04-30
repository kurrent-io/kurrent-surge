using System.Collections.Concurrent;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Tests;

sealed class TestRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<Record> _records = new();

    public Func<ProcessRecordContext, ValueTask>? OnProcessRecord { get; set; }

    public Func<CompleteContext, ValueTask>? OnComplete { get; set; }

    public CompleteContext? Complete { get; private set; }

    public Record[] Records => _records.ToArray();

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        if (OnProcessRecord is not null)
            await OnProcessRecord(context);

        // Enqueue iff the framework or user will track this record. With
        // AutoTrack=true, that's "no Stop request" (auto-track will run); the user
        // can also opt in by calling Track() explicitly before StopProcessing().
        if (!context.StopRequested || context.TrackedByUser)
            _records.Enqueue(context.Record);
    }

    ValueTask IProcessor.OnComplete(CompleteContext context) {
        Complete = context;
        return OnComplete?.Invoke(context) ?? ValueTask.CompletedTask;
    }
}

sealed class TestRecordBatchProcessor(int maxBatchSize, TimeSpan batchWindow) : RecordBatchProcessor(maxBatchSize, batchWindow) {
    readonly ConcurrentQueue<RecordBatch>      _batches     = new();
    readonly ConcurrentQueue<CheckpointContext> _checkpoints = new();

    public Func<BatchContext,      ValueTask>? OnProcessBatch    { get; set; }
    public Func<ConnectedContext,  ValueTask>? OnConnectedHook   { get; set; }
    public Func<CheckpointContext, ValueTask>? OnCheckpointHook  { get; set; }

    public ConnectedContext? Connected { get; private set; }
    public CompleteContext?  Complete  { get; private set; }

    public RecordBatch[]                Batches     => _batches.ToArray();
    public IReadOnlyList<Record>        Records     => _batches.SelectMany(b => b).ToList();
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

    public override ValueTask OnComplete(CompleteContext context) {
        Complete = context;
        return ValueTask.CompletedTask;
    }
}
