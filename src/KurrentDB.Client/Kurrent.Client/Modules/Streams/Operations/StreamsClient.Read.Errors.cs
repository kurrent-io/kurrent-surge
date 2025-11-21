using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
public partial class InspectRecordError : IVariantException<
    LogPositionNotFoundException,
    Exception> {
    public static InspectRecordError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<InspectRecordError>(
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
        // protocol v1 Tombstoned special case
        // to be removed when v2 is operational and v1 is deprecated
        if (ex is RpcException { StatusCode: StatusCode.FailedPrecondition } rex && rex.Trailers.GetValue("exception") == "stream-deleted")
            return new StreamTombstonedException(new Google.Rpc.Status {
                Code    = (int)rex.StatusCode,
                Message = rex.Message
            });

        return ex.ToExceptionalErrorResult<ReadError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From,
            LogPositionNotFoundException.From
        );
    }
}
