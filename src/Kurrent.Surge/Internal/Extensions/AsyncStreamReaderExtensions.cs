using Grpc.Core;

namespace Kurrent.Toolkit.Grpc;

static class AsyncStreamReaderSafetyExtensions {
    extension<T>(IAsyncStreamReader<T> stream) {
        /// <summary>
        /// Advances the stream, returning <c>false</c> on cancellation instead of throwing.
        /// gRPC surfaces token cancellation as <see cref="RpcException"/> with
        /// <see cref="StatusCode.Cancelled"/> rather than <see cref="OperationCanceledException"/>.
        /// Both are treated as normal termination so callers can use this in yield-based
        /// iterators where try/catch is not allowed.
        /// </summary>
        public async ValueTask<bool> MoveNextSafe(CancellationToken ct) {
            try {
                return await stream.MoveNext(ct).ConfigureAwait(false);
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
                return false;
            }
            catch (OperationCanceledException) {
                return false;
            }
        }
    }
}
