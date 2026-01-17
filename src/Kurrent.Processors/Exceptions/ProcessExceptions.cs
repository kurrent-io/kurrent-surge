using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public class FailedToCreateWorkerException(Exception innerException) : KurrentException(Code, ErrorMessage, innerException) {
    static readonly ErrorCode Code         = ErrorCode.From("WORKER_CREATION_FAILED");
    static readonly string    ErrorMessage = "Failed to create record processor instance.";
}
