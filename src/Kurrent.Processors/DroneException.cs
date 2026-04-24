using Kurrent.Client.Exceptions;
using Kurrent.Processors.Resources;

namespace Kurrent.Processors;

[PublicAPI]
public class DroneException(ErrorCode code, string reason, Exception innerException) : KurrentException(code, reason, innerException) {
    public static DroneException ProcessRecordFailed(Exception innerException) =>
        new("PROCESS_RECORD_FAILED", ErrorMessages.Drone_ProcessRecordFailed, innerException);

    public static DroneException OnConnectedFailed(Exception innerException) =>
        new("ON_CONNECTED_FAILED", "An error occurred in the connection handler.", innerException);

    public static DroneException OnCheckpointFailed(Exception innerException) =>
        new("ON_CHECKPOINT_FAILED", ErrorMessages.Drone_OnCheckpointFailed, innerException);

    public static DroneException OnErrorFailed(Exception innerException) =>
        new("ON_ERROR_FAILED", ErrorMessages.Drone_OnErrorFailed, innerException);

    public static DroneException OnCompleteFailed(Exception innerException) =>
        new("ON_COMPLETE_FAILED", ErrorMessages.Drone_OnCompleteFailed, innerException);
}
