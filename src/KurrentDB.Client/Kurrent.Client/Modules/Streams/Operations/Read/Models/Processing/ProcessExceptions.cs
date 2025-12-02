using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Streams;

[PublicAPI]
public class ProcessorCreationException(Exception innerException) : KurrentException(Code, ErrorMessage, innerException) {
    static readonly ErrorCode Code         = ErrorCode.From("PROCESSOR_CREATION_FAILED");
    static readonly string    ErrorMessage = "Failed to create record processor instance.";
}
