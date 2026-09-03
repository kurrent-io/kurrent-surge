using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Kurrent.Toolkit.Grpc.Interceptors;

/// <summary>
/// Injects a fixed set of headers on every call. A caller-supplied value wins over a
/// static header with the same key, and the caller's metadata instance is never mutated.
/// </summary>
sealed class ClientHeadersInterceptor : Interceptor {
    // The entries are immutable and safely shared across calls: building them once removes
    // three Metadata.Entry allocations plus a dictionary-enumerator box from every gRPC call.
    readonly Metadata.Entry[] _headers;

    public ClientHeadersInterceptor(IReadOnlyDictionary<string, string> staticHeaders) =>
        _headers = staticHeaders
            .Select(header => new Metadata.Entry(header.Key, header.Value))
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
        var existing = context.Options.Headers;

        // Build a fresh collection instead of adding into the caller's: a Metadata instance
        // reused across calls would otherwise accumulate the static entries on every call,
        // and a caller-set key would be sent twice.
        var effective = new Metadata();

        if (existing is not null)
            foreach (var entry in existing)
                effective.Add(entry);

        foreach (var header in _headers)
            if (existing is null || !ContainsKey(existing, header.Key))
                effective.Add(header);

        return new(context.Method, context.Host, context.Options.WithHeaders(effective));

        // Grpc.Core lowercases entry keys on construction and the static keys are declared
        // lowercase, so ordinal comparison is exact.
        static bool ContainsKey(Metadata metadata, string key) {
            foreach (var entry in metadata)
                if (string.Equals(entry.Key, key, StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
