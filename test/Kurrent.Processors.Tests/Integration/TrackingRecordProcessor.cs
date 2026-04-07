using System.Collections.Concurrent;
using Kurrent.Client.Streams;

namespace Kurrent.Processors.Tests;

sealed class TestRecordProcessor : IRecordProcessor {
    readonly ConcurrentQueue<Record> _records = new();

    public Func<ProcessRecordContext, ValueTask>? OnProcessRecord { get; set; }

    public Func<ErrorContext, ValueTask>? OnError { get; set; }

    public Func<CompleteContext, ValueTask>? OnComplete { get; set; }

    public ErrorContext? Error { get; private set; }

    public CompleteContext? Complete { get; private set; }

    public Record[] Records => _records.ToArray();

    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        if (OnProcessRecord is not null)
            await OnProcessRecord(context);

        // Don't enqueue records that won't be tracked (StopBeforeTrack skips tracking)
        if (context.StopProcessingRequest.Trigger != StopProcessingTrigger.StopBeforeTrack)
            _records.Enqueue(context.Record);
    }

    ValueTask IRecordProcessor.OnError(ErrorContext context) {
        Error = context;
        return OnError?.Invoke(context) ?? ValueTask.CompletedTask;
    }

    ValueTask IRecordProcessor.OnComplete(CompleteContext context) {
        Complete = context;
        return OnComplete?.Invoke(context) ?? ValueTask.CompletedTask;
    }
}
