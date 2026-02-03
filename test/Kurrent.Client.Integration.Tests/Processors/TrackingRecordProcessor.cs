using System.Collections.Concurrent;
using Kurrent.Client.Streams;
using Kurrent.Processors;

namespace Kurrent.Client.Tests.Processors;

sealed class TestRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<Record> _records = new();

    public Func<ProcessRecordsContext, ValueTask>? OnProcessRecords { get; set; }

    public ShutdownContext? Shutdown { get; private set; }

    public Record[] Records => _records.ToArray();

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        foreach (var record in context.Records)
            _records.Enqueue(record);

        return OnProcessRecords?.Invoke(context) ?? ValueTask.CompletedTask;
    }

    ValueTask IRecordProcessor.OnShutdown(ShutdownContext context) {
        Shutdown = context;
        return ValueTask.CompletedTask;
    }
}
