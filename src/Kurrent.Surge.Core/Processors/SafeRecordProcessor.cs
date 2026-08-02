namespace Kurrent.Surge.Streaming;

sealed class SafeRecordProcessor(IRecordProcessor inner) : IRecordProcessor {
    public async ValueTask ProcessRecord(ProcessRecordContext context) {
        try {
            await inner.ProcessRecord(context).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            throw DroneException.ProcessRecordFailed(ex);
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
