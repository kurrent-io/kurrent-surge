namespace Kurrent.Processors;

sealed class SafeRecordBatchProcessor(IRecordBatchProcessor inner) : IRecordBatchProcessor {
    public int      MaxBatchSize => inner.MaxBatchSize;
    public TimeSpan BatchWindow  => inner.BatchWindow;

    public async ValueTask ProcessBatch(BatchContext context) {
        try {
            await inner.ProcessBatch(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.ProcessBatchFailed(ex);
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
