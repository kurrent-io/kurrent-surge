namespace Kurrent.Surge.Streaming;

sealed class SafeRecordBatchProcessor(IRecordBatchProcessor inner) : IRecordBatchProcessor {
    public async ValueTask ProcessBatch(BatchContext context) {
        try {
            await inner.ProcessBatch(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.ProcessBatchFailed(ex);
        }
    }

    public async ValueTask OnInitialize(InitializeContext context) {
        try {
            await inner.OnInitialize(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.OnInitializeFailed(ex);
        }
    }

    public async ValueTask OnConnected(ConnectedContext context) {
        try {
            await inner.OnConnected(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.OnConnectedFailed(ex);
        }
    }

    public async ValueTask OnCheckpoint(CheckpointContext context) {
        try {
            await inner.OnCheckpoint(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.OnCheckpointFailed(ex);
        }
    }
}
