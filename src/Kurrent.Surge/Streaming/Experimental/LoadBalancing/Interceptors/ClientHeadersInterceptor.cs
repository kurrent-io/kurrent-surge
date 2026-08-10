using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Injects the client identity headers on every call, creating the metadata collection
/// when the caller supplied none.
/// </summary>
sealed class ClientHeadersInterceptor : Interceptor {
    // The entries are immutable and safely shared across calls: building them once removes
    // three Metadata.Entry allocations plus a dictionary-enumerator box from every gRPC call.
    readonly Metadata.Entry[] _headers;

    public ClientHeadersInterceptor(IReadOnlyDictionary<string, string> staticHeaders) =>
        _headers = staticHeaders.Select(header => new Metadata.Entry(header.Key, header.Value)).ToArray();

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithHeaders(context));

    public override TResponse BlockingUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        BlockingUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithHeaders(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithHeaders(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithHeaders(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithHeaders(context));

    ClientInterceptorContext<TRequest, TResponse> WithHeaders<TRequest, TResponse>(
        in ClientInterceptorContext<TRequest, TResponse> context
    ) where TRequest : class where TResponse : class {
        var headers = context.Options.Headers;

        var result = headers is null
            ? new(context.Method, context.Host, context.Options.WithHeaders(headers = []))
            : context;

        foreach (var header in _headers)
            headers.Add(header);

        return result;
    }
}
