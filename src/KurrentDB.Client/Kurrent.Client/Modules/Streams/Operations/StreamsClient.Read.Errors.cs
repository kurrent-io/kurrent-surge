using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
public partial class InspectRecordError : IVariantException<
    StreamTombstonedException,
    StreamDeletedException,
    LogPositionNotFoundException,
    Exception> {
    public static InspectRecordError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<InspectRecordError>(
            StreamTombstonedException.From,
            StreamDeletedException.From,
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
