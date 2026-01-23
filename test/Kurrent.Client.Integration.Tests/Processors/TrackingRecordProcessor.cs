using Kurrent.Client.Streams;
using Serilog;

namespace Kurrent.Client.Tests.Processors;

/// <summary>
/// A test processor that tracks all lifecycle calls and records for assertions.
/// </summary>
sealed class TrackingRecordProcessor : IRecordProcessor {
    readonly object _lock = new();
    readonly List<(string Phase, DateTime Timestamp)> _callOrder        = [];
    readonly List<CheckpointCompleteContext>          _checkpoints      = [];
    readonly List<Record>                             _processedRecords = [];

    public IReadOnlyList<Record> ProcessedRecords {
        get { lock (_lock) return _processedRecords.ToList(); }
    }

    public IReadOnlyList<CheckpointCompleteContext> Checkpoints {
        get { lock (_lock) return _checkpoints.ToList(); }
    }

    public IReadOnlyList<(string Phase, DateTime Timestamp)> CallOrder {
        get { lock (_lock) return _callOrder.ToList(); }
    }

    public bool InitializeCalled        { get; private set; }
    public bool ShutdownCalled          { get; private set; }
    public int  ProcessRecordsCalls     { get; private set; }
    public int  CheckpointCompleteCalls { get; private set; }

    public InitializationContext? InitializeContext { get; private set; }
    public ShutdownContext?       ShutdownContext   { get; private set; }

    public Action<Record>?                             OnRecordProcessed            { get; set; }
    public Action<ProcessRecordsContext>?              OnBatchProcessed             { get; set; }
    public Func<ProcessRecordsContext, ValueTask>?     OnRecordsProcessed           { get; set; }
    public Func<InitializationContext, ValueTask>?     OnInitializeCallback         { get; set; }
    public Func<ProcessRecordsContext, ValueTask>?     OnProcessRecordsCallback     { get; set; }
    public Func<CheckpointCompleteContext, ValueTask>? OnCheckpointCompleteCallback { get; set; }
    public Func<ShutdownContext, ValueTask>?           OnShutdownCallback           { get; set; }

    public bool InitializeCalledFirst {
        get {
            lock (_lock) {
                return _callOrder.Count > 0 && _callOrder[0].Phase == "Initialize";
            }
        }
    }

    public async ValueTask OnInitialize(InitializationContext context) {
        InitializeCalled  = true;
        InitializeContext = context;

        lock (_lock) {
            _callOrder.Add(("Initialize", DateTime.UtcNow));
        }

        Log.Debug(
            "Processor initialized: ProcessorId={ProcessorId}, WorkerId={WorkerId}",
            context.Settings.ProcessorId, context.Settings.WorkerId
        );

        if (OnInitializeCallback is not null)
            await OnInitializeCallback(context);
    }

    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        ProcessRecordsCalls++;

        lock (_lock) {
            _callOrder.Add(("ProcessRecords", DateTime.UtcNow));
        }

        foreach (var record in context.Records) {
            int totalCount;
            lock (_lock) {
                _processedRecords.Add(record);
                totalCount = _processedRecords.Count;
            }

            Log.Debug(
                "Processed record {RecordId} @ {Position} (total: {Total})",
                record.Id, record.Position, totalCount
            );

            OnRecordProcessed?.Invoke(record);
        }

        OnBatchProcessed?.Invoke(context);

        if (OnRecordsProcessed is not null)
            await OnRecordsProcessed(context);

        if (OnProcessRecordsCallback is not null)
            await OnProcessRecordsCallback(context);
    }

    public async ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        CheckpointCompleteCalls++;

        lock (_lock) {
            _checkpoints.Add(context);
            _callOrder.Add(("CheckpointComplete", DateTime.UtcNow));
        }

        Log.Debug(
            "Checkpoint complete: TotalCheckpoints={TotalCheckpoints}, BarrierId={BarrierId}",
            context.Progress.Checkpoint.TotalCheckpoints,
            context.Progress.Checkpoint.LastBarrierId
        );

        if (OnCheckpointCompleteCallback is not null)
            await OnCheckpointCompleteCallback(context);
    }

    public async ValueTask OnShutdown(ShutdownContext context) {
        ShutdownCalled  = true;
        ShutdownContext = context;

        lock (_lock) {
            _callOrder.Add(("Shutdown", DateTime.UtcNow));
        }

        if (context.Reason == ShutdownReason.Error)
            Log.Error(
                "Shutdown due to error: {Error} after {TotalRecords} records",
                context.Error, context.Progress.Throughput.TotalRecords
            );
        else
            Log.Debug(
                "Shutdown ({Reason}): {TotalRecords} records in {Duration}",
                context.Reason, context.Progress.Throughput.TotalRecords,
                context.Progress.Throughput.Elapsed
            );

        if (OnShutdownCallback is not null)
            await OnShutdownCallback(context);
    }

    public void Reset() {
        lock (_lock) {
            _processedRecords.Clear();
            _checkpoints.Clear();
            _callOrder.Clear();
        }
        InitializeCalled        = false;
        ShutdownCalled          = false;
        ProcessRecordsCalls     = 0;
        CheckpointCompleteCalls = 0;
        InitializeContext       = null;
        ShutdownContext         = null;
    }
}

/// <summary>
/// A test processor that throws exceptions at specified lifecycle points.
/// </summary>
sealed class ThrowingRecordProcessor : IRecordProcessor {
    public Exception? ThrowOnInitialize         { get; set; }
    public Exception? ThrowOnProcessRecords     { get; set; }
    public Exception? ThrowOnCheckpointComplete { get; set; }
    public Exception? ThrowOnShutdown           { get; set; }

    public int ProcessRecordsCallCount { get; private set; }

    public ValueTask OnInitialize(InitializationContext context) {
        if (ThrowOnInitialize is not null)
            throw ThrowOnInitialize;

        return ValueTask.CompletedTask;
    }

    public ValueTask ProcessRecords(ProcessRecordsContext context) {
        ProcessRecordsCallCount++;
        if (ThrowOnProcessRecords is not null)
            throw ThrowOnProcessRecords;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnCheckpointComplete(CheckpointCompleteContext context) {
        if (ThrowOnCheckpointComplete is not null)
            throw ThrowOnCheckpointComplete;

        return ValueTask.CompletedTask;
    }

    public ValueTask OnShutdown(ShutdownContext context) {
        if (ThrowOnShutdown is not null)
            throw ThrowOnShutdown;

        return ValueTask.CompletedTask;
    }
}

// /// <summary>
// /// Factory for creating tracking processors.
// /// </summary>
// sealed class TrackingRecordProcessorFactory : IRecordProcessorFactory {
//     readonly List<TrackingRecordProcessor> _createdProcessors = [];
//
//     public int                                    CreateCount          => _createdProcessors.Count;
//     public IReadOnlyList<TrackingRecordProcessor> CreatedProcessors    => _createdProcessors;
//     public TrackingRecordProcessor?               LastCreatedProcessor => _createdProcessors.Count > 0 ? _createdProcessors[^1] : null;
//
//     public IRecordProcessor CreateProcessor() {
//         var processor = new TrackingRecordProcessor();
//         _createdProcessors.Add(processor);
//         return processor;
//     }
// }
