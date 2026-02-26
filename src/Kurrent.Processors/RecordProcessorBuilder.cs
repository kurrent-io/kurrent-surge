namespace Kurrent.Processors;

/// <summary>
/// Builds an <see cref="IRecordProcessor"/> from delegate callbacks.
/// </summary>
[PublicAPI]
public sealed class RecordProcessorBuilder {
    Func<CheckpointContext, ValueTask>     _onCheckpoint   = static _ => ValueTask.CompletedTask;
    Func<CompleteContext, ValueTask>       _onComplete     = static _ => ValueTask.CompletedTask;
    Func<ErrorContext, ValueTask>          _onError        = static _ => ValueTask.CompletedTask;
    Func<ProcessRecordContext, ValueTask> _processRecord = static _ => ValueTask.CompletedTask;
    bool                                  _processRecordSet;

    public RecordProcessorBuilder ProcessRecordAsync(Func<ProcessRecordContext, ValueTask> handler) {
        _processRecord    = handler;
        _processRecordSet = true;
        return this;
    }

    public RecordProcessorBuilder OnCheckpointAsync(Func<CheckpointContext, ValueTask> handler) {
        _onCheckpoint = handler;
        return this;
    }

    public RecordProcessorBuilder OnErrorAsync(Func<ErrorContext, ValueTask> handler) {
        _onError = handler;
        return this;
    }

    public RecordProcessorBuilder OnCompleteAsync(Func<CompleteContext, ValueTask> handler) {
        _onComplete = handler;
        return this;
    }

    public RecordProcessorBuilder ProcessRecord(Action<ProcessRecordContext> handler) =>
        ProcessRecordAsync(ctx => {
                handler(ctx);
                return ValueTask.CompletedTask;
            }
        );

    public RecordProcessorBuilder OnCheckpoint(Action<CheckpointContext> handler) =>
        OnCheckpointAsync(ctx => {
                handler(ctx);
                return ValueTask.CompletedTask;
            }
        );

    public RecordProcessorBuilder OnError(Action<ErrorContext> handler) =>
        OnErrorAsync(ctx => {
                handler(ctx);
                return ValueTask.CompletedTask;
            }
        );

    public RecordProcessorBuilder OnComplete(Action<CompleteContext> handler) =>
        OnCompleteAsync(ctx => {
                handler(ctx);
                return ValueTask.CompletedTask;
            }
        );

    internal IRecordProcessor Build() {
        if (!_processRecordSet)
            throw new InvalidOperationException("ProcessRecord handler is required.");

        return new DelegateRecordProcessor(
            _processRecord,
            _onCheckpoint,
            _onError,
            _onComplete
        );
    }
}

sealed class DelegateRecordProcessor(
    Func<ProcessRecordContext, ValueTask> processRecord,
    Func<CheckpointContext, ValueTask> onCheckpoint,
    Func<ErrorContext, ValueTask> onError,
    Func<CompleteContext, ValueTask> onComplete
) : IRecordProcessor {
    public ValueTask ProcessRecord(ProcessRecordContext context) => processRecord(context);

    public ValueTask OnCheckpoint(CheckpointContext context) => onCheckpoint(context);

    public ValueTask OnError(ErrorContext context) => onError(context);

    public ValueTask OnComplete(CompleteContext context) => onComplete(context);
}
