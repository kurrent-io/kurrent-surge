using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kurrent.Surge.Client.LoadBalancing;

/// <summary>
/// Injects the client identity headers on every call, creating the metadata collection
/// when the caller supplied none.
/// </summary>
sealed class ClientHeadersInterceptor : Interceptor {
    // global::-qualified: this namespace nests under both Kurrent.Surge.Client (which
    // declares its own domain Metadata type) and Kurrent (which has a Kurrent.Grpc
    // namespace), so an unqualified or partially-qualified Grpc.Core.Metadata resolves
    // to the wrong type instead of the gRPC one.

    // The entries are immutable and safely shared across calls: building them once removes
    // three Metadata.Entry allocations plus a dictionary-enumerator box from every gRPC call.
    readonly global::Grpc.Core.Metadata.Entry[] _headers;

    public ClientHeadersInterceptor(IReadOnlyDictionary<string, string> staticHeaders) =>
        _headers = staticHeaders
            .Select(header => new global::Grpc.Core.Metadata.Entry(header.Key, header.Value))
            .ToArray();

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
        var result = context.Options.Headers is not { } headers
            ? new(context.Method, context.Host, context.Options.WithHeaders(headers = []))
            : context;

        foreach (var header in _headers)
            headers.Add(header);

        return result;
    }
}
