using Kurrent.Client.Exceptions;
using Kurrent.Variant;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Kurrent.Client.Streams;

[PublicAPI]
public partial class GetStreamInfoError : IVariantException<Exception> {
    public static GetStreamInfoError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetStreamInfoError>();
}

[PublicAPI]
public partial class DeleteStreamError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    Exception
> {
    public static DeleteStreamError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<DeleteStreamError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamDeletedException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e)
        );
}

[PublicAPI]
public partial class TombstoneStreamError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    Exception
> {
    public static TombstoneStreamError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<TombstoneStreamError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamDeletedException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e)
        );
}

[PublicAPI]
public partial class TruncateStreamError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    Exception
> {
    public static TruncateStreamError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<TruncateStreamError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamDeletedException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e),
            static (s, e) => StreamRevisionConflictException.From(s, e)
        );
}

[PublicAPI]
public partial class SetStreamMetadataError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    StreamRevisionConflictException,
    Exception
> {
    public static SetStreamMetadataError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<SetStreamMetadataError>(
            static (s, e) => ConsistencyViolationException.From(s, e),
            static (s, e) => MaxAppendRecordSizeExceededException.From(s, e),
            static (s, e) => MaxAppendTransactionSizeExceededException.From(s, e));
}

[PublicAPI]
public partial class GetStreamMetadataError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    Exception
> {
    public static GetStreamMetadataError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<GetStreamMetadataError>(
            static (s, e) => StreamNotFoundException.From(s, e),
            static (s, e) => StreamDeletedException.From(s, e),
            static (s, e) => StreamTombstonedException.From(s, e)
        );
}
