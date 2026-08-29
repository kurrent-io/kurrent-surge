using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kurrent.Grpc.Interceptors;

/// <summary>
/// Applies a default deadline to calls that do not carry one. A deadline set on the call
/// always wins.
/// </summary>
/// <remarks>
/// The deadline covers the whole call, so on a server-streaming RPC it bounds the stream
/// rather than the first response. A subscription under a finite deadline is terminated
/// when that deadline fires.
/// </remarks>
sealed class CallDeadlineInterceptor(TimeSpan deadline) : Interceptor {
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDeadline(context));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDeadline(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithDeadline(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithDeadline(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithDeadline(context));

    ClientInterceptorContext<TRequest, TResponse> WithDeadline<TRequest, TResponse>(
        in ClientInterceptorContext<TRequest, TResponse> context
    ) where TRequest : class where TResponse : class =>
        context.Options.Deadline is not null
            ? context
            : new(context.Method, context.Host, context.Options.WithDeadline(DateTime.UtcNow.Add(deadline)));
}
