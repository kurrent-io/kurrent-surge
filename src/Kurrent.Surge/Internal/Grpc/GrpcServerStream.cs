using Grpc.Core;

namespace Kurrent.Toolkit.Grpc;

/// <summary>
/// A self-disposing <see cref="IAsyncEnumerable{T}"/> over a gRPC server-streaming call.
/// Equivalent to <c>ReadAllAsync</c> but owns the <see cref="AsyncServerStreamingCall{T}"/>
/// lifecycle and optionally translates <see cref="RpcException"/> into domain exceptions.
/// </summary>
readonly struct GrpcServerStream<T>(
    AsyncServerStreamingCall<T> call,
    Func<RpcException, Exception>? translateError = null
) : IAsyncEnumerable<T> {

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default) =>
        new AdvancedEnumerator(call, translateError, ct);

    readonly struct AdvancedEnumerator(
        AsyncServerStreamingCall<T> call,
        Func<RpcException, Exception>? translateError,
        CancellationToken ct
    ) : IAsyncEnumerator<T> {
        public T Current => call.ResponseStream.Current;

        public async ValueTask<bool> MoveNextAsync() {
            try {
                return await call.ResponseStream.MoveNext(ct).ConfigureAwait(false);
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
                return false;
            }
            catch (OperationCanceledException) {
                return false;
            }
            catch (RpcException rex) when (translateError is not null) {
                throw translateError(rex);
            }
        }

        public ValueTask DisposeAsync() {
            call.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

static class AsyncServerStreamingCallExtensions {
    /// <summary>
    /// Returns a self-disposing <see cref="IAsyncEnumerable{T}"/> over the streaming call.
    /// The call is disposed when enumeration completes or the enumerator is disposed.
    /// </summary>
    public static GrpcServerStream<T> All<T>(this AsyncServerStreamingCall<T> call) =>
        new(call, translateError: null);

    /// <summary>
    /// Returns a self-disposing <see cref="IAsyncEnumerable{T}"/> over the streaming call,
    /// translating <see cref="RpcException"/> via <paramref name="translateError"/>.
    /// </summary>
    public static GrpcServerStream<T> All<T>(
        this AsyncServerStreamingCall<T> call,
        Func<RpcException, Exception> translateError
    ) => new(call, translateError);
}

