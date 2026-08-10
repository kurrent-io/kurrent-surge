using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Injects the client identity headers and the <c>requires-leader</c> routing header on
/// every call, creating the metadata collection when the caller supplied none.
/// </summary>
sealed class ClientHeadersInterceptor(
    IReadOnlyDictionary<string, string> staticHeaders,
    bool requiresLeader,
    params string[] requiresLeaderExcludedMethods
) : Interceptor {
    const string RequiresLeaderKey = "requires-leader";

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

        foreach (var header in staticHeaders)
            headers.Add(header.Key, header.Value);

        if (!requiresLeaderExcludedMethods.Contains(context.Method.Name, StringComparer.OrdinalIgnoreCase))
            headers.Add(RequiresLeaderKey, requiresLeader ? bool.TrueString : bool.FalseString);

        return result;
    }
}
