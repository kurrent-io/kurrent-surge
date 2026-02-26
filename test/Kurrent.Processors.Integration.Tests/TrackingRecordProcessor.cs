using System.Collections.Concurrent;
using Kurrent.Client.Streams;
using Kurrent.Processors;

namespace Kurrent.Processors.Tests;

sealed class TestRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<Record> _records = new();

    public Func<ProcessRecordContext, ValueTask>? OnProcessRecord { get; set; }

    public ErrorContext? Error { get; private set; }

    public CompleteContext? Complete { get; private set; }

    public Record[] Records => _records.ToArray();

    public ValueTask ProcessRecord(ProcessRecordContext context) {
        _records.Enqueue(context.Record);

        return OnProcessRecord?.Invoke(context) ?? ValueTask.CompletedTask;
    }

    ValueTask IRecordProcessor.OnError(ErrorContext context) {
        Error = context;
        return ValueTask.CompletedTask;
    }

    ValueTask IRecordProcessor.OnComplete(CompleteContext context) {
        Complete = context;
        return ValueTask.CompletedTask;
    }
}
