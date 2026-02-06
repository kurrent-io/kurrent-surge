using System.Collections.Concurrent;
using Kurrent.Client.Streams;
using Kurrent.Processors;

namespace Kurrent.Client.Tests.Processors;

sealed class TestRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<Record> _records = new();

    public Func<ProcessRecordsContext, ValueTask>? OnProcessRecords { get; set; }

    public ErrorContext? Error { get; private set; }

    public CompleteContext? Complete { get; private set; }

    public Record[] Records => _records.ToArray();

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records)
            _records.Enqueue(record);

        return OnProcessRecords?.Invoke(context) ?? ValueTask.CompletedTask;
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
