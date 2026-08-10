using BenchmarkDotNet.Attributes;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Experimental.LoadBalancing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Benchmarks;

/// <summary>
/// Prices the balanced channel's per-call and per-message interceptor overhead: identity-header
/// injection runs on every gRPC call, and the leader-change wrapper inspects every streamed
/// message for the not-leader fault. The fake invoker's own allocations are identical across
/// runs, so deltas isolate the interceptor cost.
/// </summary>
[Config(typeof(BenchmarkConfig))]
[BenchmarkCategory("LoadBalancing")]
public class InterceptorBenchmarks {
    static readonly Marshaller<string> Marshaller = Marshallers.Create(_ => [], _ => "");

    static readonly Method<string, string> UnaryMethod =
        new(MethodType.Unary, "bench", "unary", Marshaller, Marshaller);

    static readonly Method<string, string> StreamMethod =
        new(MethodType.ServerStreaming, "bench", "stream", Marshaller, Marshaller);

    CallInvoker                _headerInvoker = null!;
    IAsyncStreamReader<string> _wrappedReader = null!;

    [GlobalSetup]
    public void Setup() {
        _headerInvoker = new NoOpCallInvoker().Intercept(new ClientHeadersInterceptor(new Dictionary<string, string> {
            ["kurrentdb.client.name"]    = "bench",
            ["kurrentdb.client.version"] = "1.0.0",
            ["connection-name"]          = "bench-conn"
        }));

        var interceptor = new LeaderChangeInterceptor(_ => { }, NullLogger.Instance);

        var call = interceptor.AsyncServerStreamingCall(
            "req",
            new ClientInterceptorContext<string, string>(StreamMethod, null, new CallOptions()),
            (_, _) => new AsyncServerStreamingCall<string>(
                new CompletedStreamReader(),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => [],
                () => { }));

        _wrappedReader = call.ResponseStream;
    }

    [Benchmark]
    public AsyncUnaryCall<string> UnaryCallWithIdentityHeaders() =>
        _headerInvoker.AsyncUnaryCall(UnaryMethod, null, new CallOptions(), "req");

    [Benchmark]
    public Task<bool> StreamedMessageMoveNext() =>
        _wrappedReader.MoveNext(CancellationToken.None);

    sealed class NoOpCallInvoker : CallInvoker {
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            default!;

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            new(Task.FromResult(default(TResponse)!), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }

    sealed class CompletedStreamReader : IAsyncStreamReader<string> {
        static readonly Task<bool> True = Task.FromResult(true);

        public string Current => "msg";

        public Task<bool> MoveNext(CancellationToken cancellationToken) => True;
    }
}
