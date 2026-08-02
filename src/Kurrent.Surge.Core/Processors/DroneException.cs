using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Streaming;

[PublicAPI]
public class DroneException(ErrorCode code, string reason, Exception innerException) : KurrentException(code, reason, innerException) {
    public static DroneException ProcessRecordFailed(Exception innerException) =>
        new("PROCESS_RECORD_FAILED", "An error occurred while processing a record.", innerException);

    public static DroneException ProcessBatchFailed(Exception innerException) =>
        new("PROCESS_BATCH_FAILED", "An error occurred while processing the record batch.", innerException);

    public static DroneException OnInitializeFailed(Exception innerException) =>
        new("ON_INITIALIZE_FAILED", "An error occurred in the initialization handler.", innerException);

    public static DroneException OnConnectedFailed(Exception innerException) =>
        new("ON_CONNECTED_FAILED", "An error occurred in the connection handler.", innerException);

    public static DroneException OnCheckpointFailed(Exception innerException) =>
        new("ON_CHECKPOINT_FAILED", "An error occurred during checkpoint handling.", innerException);
}
