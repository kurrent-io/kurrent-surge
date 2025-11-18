using Kurrent.Client.Exceptions;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

[PublicAPI]
public partial class InspectRecordError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException,
    StreamTombstonedException,
    Exception> {
    public static InspectRecordError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<InspectRecordError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From);
}

[PublicAPI]
public partial class ReadError : IVariantException<
    StreamNotFoundException,
    StreamDeletedException, // cannot read a deleted stream (but you can subscribe to it ;)
    StreamTombstonedException,
    Exception> {
    public static ReadError FromEx(Exception ex) =>
        ex.ToExceptionalErrorResult<ReadError>(
            StreamNotFoundException.From,
            StreamDeletedException.From,
            StreamTombstonedException.From);
}
