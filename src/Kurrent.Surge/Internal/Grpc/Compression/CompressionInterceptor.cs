using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Client;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Grpc.Compression;

/// <summary>
/// Enforces the configured compression algorithm on every outgoing call, creating the
/// metadata collection when the caller supplied none and overriding any mismatched
/// algorithm already present. An explicit WithoutCompression ("identity") opt-out is
/// left alone — it's the one recognized per-call bypass.
/// </summary>
sealed class CompressionInterceptor(CompressionAlgorithm algorithm) : Interceptor {
    // The entry is immutable and safely shared across calls: building it once removes a
    // Metadata.Entry allocation from every gRPC call.
    readonly Metadata.Entry _header = new(GrpcCompression.CompressionRequestAlgorithmHeader, algorithm.ToEncodingName());

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithCompression(context));

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithCompression(context));

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(request, WithCompression(context));

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) => continuation(WithCompression(context));

    ClientInterceptorContext<TRequest, TResponse> WithCompression<TRequest, TResponse>(
        in ClientInterceptorContext<TRequest, TResponse> context
    ) where TRequest : class where TResponse : class {
        var result = context.Options.Headers is not { } headers
            ? new(context.Method, context.Host, context.Options.WithHeaders(headers = []))
            : context;

        headers.EnforceCompression(_header);

        return result;
    }
}
