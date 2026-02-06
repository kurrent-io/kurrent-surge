namespace Kurrent.Processors;

sealed class SafeRecordProcessor(IRecordProcessor inner) : IRecordProcessor {
    public async ValueTask ProcessRecords(ProcessRecordsContext context) {
        try {
            await inner.ProcessRecords(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw KurrentWorkerException.ProcessRecordsFailed(ex);
        }
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        try {
            await inner.OnCheckpoint(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw KurrentWorkerException.OnCheckpointFailed(ex);
        }
    }

    public async ValueTask OnError(ErrorContext context) {
        try {
            await inner.OnError(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw KurrentWorkerException.OnErrorFailed(ex);
        }
    }

    public async ValueTask OnComplete(CompleteContext context) {
        try {
            await inner.OnComplete(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw KurrentWorkerException.OnCompleteFailed(ex);
        }
    }
}
