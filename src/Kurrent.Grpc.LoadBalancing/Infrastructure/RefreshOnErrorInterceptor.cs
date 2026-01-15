using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Kurrent.Grpc.LoadBalancing;

/// <summary>
/// gRPC interceptor that triggers topology refresh based on error policy.
/// </summary>
/// <remarks>
/// <para>
/// When a gRPC call fails with an <see cref="RpcException"/> that matches
/// the configured policy, the interceptor invokes the refresh action to
/// trigger topology re-discovery.
/// </para>
/// <para>
/// This is simpler than HTTP-level handlers and correctly catches gRPC errors.
/// </para>
/// </remarks>
internal sealed class RefreshOnErrorInterceptor : Interceptor {
    readonly ShouldRefreshTopology _policy;
    readonly Action _onRefresh;
    readonly ILogger? _logger;

    public RefreshOnErrorInterceptor(
        ShouldRefreshTopology policy,
        Action onRefresh,
        ILogger? logger = null) {

        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _onRefresh = onRefresh ?? throw new ArgumentNullException(nameof(onRefresh));
        _logger = logger;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation) {

        var call = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            HandleResponse(call.ResponseAsync),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation) {

        var call = continuation(request, context);

        // For streaming, we wrap the response stream to catch errors
        return new AsyncServerStreamingCall<TResponse>(
            new RefreshOnErrorStreamReader<TResponse>(call.ResponseStream, _policy, _onRefresh, _logger),
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    async Task<TResponse> HandleResponse<TResponse>(Task<TResponse> responseTask) {
        try {
            return await responseTask.ConfigureAwait(false);
        }
        catch (RpcException ex) when (_policy(ex)) {
            _logger?.LogDebug(ex, "Refresh triggered by RpcException: {StatusCode}", ex.StatusCode);
            _onRefresh();
            throw;
        }
    }

    /// <summary>
    /// Stream reader wrapper that triggers refresh on error.
    /// </summary>
    sealed class RefreshOnErrorStreamReader<T> : IAsyncStreamReader<T> {
        readonly IAsyncStreamReader<T> _inner;
        readonly ShouldRefreshTopology _policy;
        readonly Action _onRefresh;
        readonly ILogger? _logger;

        public RefreshOnErrorStreamReader(
            IAsyncStreamReader<T> inner,
            ShouldRefreshTopology policy,
            Action onRefresh,
            ILogger? logger) {

            _inner = inner;
            _policy = policy;
            _onRefresh = onRefresh;
            _logger = logger;
        }

        public T Current => _inner.Current;

        public async Task<bool> MoveNext(CancellationToken cancellationToken) {
            try {
                return await _inner.MoveNext(cancellationToken).ConfigureAwait(false);
            }
            catch (RpcException ex) when (_policy(ex)) {
                _logger?.LogDebug(ex, "Refresh triggered by streaming RpcException: {StatusCode}", ex.StatusCode);
                _onRefresh();
                throw;
            }
        }
    }
}
