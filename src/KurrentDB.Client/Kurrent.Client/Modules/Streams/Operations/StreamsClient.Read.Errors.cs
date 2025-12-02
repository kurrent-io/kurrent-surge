using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
public partial class InspectRecordError : IVariantException<
    StreamTombstonedException,
    LogPositionNotFoundException,
    Exception> {
    public static InspectRecordError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<InspectRecordError>(
            StreamTombstonedException.From,
            LogPositionNotFoundException.From
        );
}

[PublicAPI]
public partial class ReadError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    LogPositionNotFoundException,
    Exception> {
    public static ReadError FromEx(Exception ex) {
        return ex.ToExceptionalErrorResult<ReadError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From,
            LogPositionNotFoundException.From
        );
    }
}

[PublicAPI]
public partial class ReadStreamError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    Exception> {
    public static ReadStreamError FromEx(Exception ex) {
        return ex.ToExceptionalErrorResult<ReadStreamError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From
        );
    }
}

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
    HeartbeatException,
    Exception> {
    public static ProcessError FromEx(Exception ex) {
        return ex.ToExceptionalErrorResult<ProcessError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From,
            LogPositionNotFoundException.From,
            InitializeException.From,
            ProcessRecordsException.From,
            HeartbeatException.From
        );
    }
}
