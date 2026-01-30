using Kurrent.Client.Exceptions;
using Kurrent.Processors;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

/// <summary>
/// Error type for record processing operations.
/// Includes processor-specific exceptions in addition to standard stream errors.
/// </summary>
[PublicAPI]
public partial class ProcessError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    LogPositionNotFoundException,
    InitializeException,
    ProcessRecordsException,
    // CheckpointException,
    // PartitionsAssignedException,
    ShutdownException,
    Exception> {
    public static ProcessError FromEx(Exception ex) {
        return ex.ToExceptionalErrorResult<ProcessError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From,
            LogPositionNotFoundException.From,
            InitializeException.From,
            ProcessRecordsException.From,
            // CheckpointException.From,
            // PartitionsAssignedException.From,
            ShutdownException.From
        );
    }
}
