using Kurrent.Client.Exceptions;
using Resources;

namespace Kurrent.Processors;

[PublicAPI]
public class KurrentWorkerException(ErrorCode code, string message, Exception innerException) : KurrentException(code, message, innerException) {
    public static KurrentWorkerException ProcessRecordsFailed(Exception innerException) =>
        new("WORKER_PROCESSRECORDS_FAILED", ErrorMessages.Worker_ProcessRecordsFailed, innerException);

    public static KurrentWorkerException OnCheckpointFailed(Exception innerException) =>
        new("WORKER_ON_CHECKPOINT_FAILED", ErrorMessages.Worker_OnCheckpointFailed, innerException);

    public static KurrentWorkerException OnErrorFailed(Exception innerException) =>
        new("WORKER_ON_ERROR_FAILED", ErrorMessages.Worker_OnErrorFailed, innerException);

    public static KurrentWorkerException OnCompleteFailed(Exception innerException) =>
        new("WORKER_ON_COMPLETE_FAILED", ErrorMessages.Worker_OnCompleteFailed, innerException);
}
