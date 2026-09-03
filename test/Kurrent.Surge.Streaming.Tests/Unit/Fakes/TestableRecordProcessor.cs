using Kurrent.Surge.Processors;
using System.Collections.Concurrent;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Streaming.Tests;

/// <summary>
/// Shared test processor with optional hooks and record collection.
/// Works as a no-op by default — set hooks to customize behavior per test.
/// </summary>
sealed class TestableRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<CheckpointContext> _checkpoints = new();
    readonly ConcurrentQueue<ConnectedContext>  _connected   = new();
    readonly ConcurrentQueue<Record>            _records     = new();

    public Func<ProcessRecordContext, ValueTask>? OnProcessRecord  { get; set; }
    public Func<CheckpointContext, ValueTask>?    OnCheckpointHook { get; set; }
    public Func<ConnectedContext, ValueTask>?     OnConnectedHook  { get; set; }

    public Record[]                         Records     => _records.ToArray();
    public IReadOnlyList<CheckpointContext> Checkpoints => _checkpoints.ToArray();
    public IReadOnlyList<ConnectedContext>  Connected   => _connected.ToArray();

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        if (OnProcessRecord is not null)
            await OnProcessRecord(context);

        if (!context.StopRequested || context.TrackedByUser)
            _records.Enqueue(context.Record);
    }

    ValueTask IProcessor.OnCheckpoint(CheckpointContext context) {
        _checkpoints.Enqueue(context);
        return OnCheckpointHook?.Invoke(context) ?? ValueTask.CompletedTask;
    }

    ValueTask IProcessor.OnConnected(ConnectedContext context) {
        _connected.Enqueue(context);
        return OnConnectedHook?.Invoke(context) ?? ValueTask.CompletedTask;
    }
}