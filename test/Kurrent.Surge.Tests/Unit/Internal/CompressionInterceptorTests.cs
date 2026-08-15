using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Grpc.Compression;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Internal;

public class CompressionInterceptorTests {
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(_ => [], _ => "");

    static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "TestService", "Append", StringMarshaller, StringMarshaller);

    [Test]
    public async ValueTask requests_compression_creating_metadata_when_absent(CancellationToken cancellationToken) {
        // Arrange
        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new CompressionInterceptor(CompressionAlgorithm.Gzip));

        // Act
        using var call = subject.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("gzip");
    }

    [Test]
    public async ValueTask requests_the_configured_algorithms_registered_encoding_name(CancellationToken cancellationToken) {
        foreach (var (algorithm, encodingName) in new (CompressionAlgorithm, string)[] {
            (CompressionAlgorithm.Gzip, "gzip"),
            (CompressionAlgorithm.Deflate, "deflate"),
            (CompressionAlgorithm.Brotli, "br")
        }) {
            // Arrange
            var invoker = new CapturingCallInvoker();
            var subject = invoker.Intercept(new CompressionInterceptor(algorithm));

            // Act
            using var call = subject.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
            await call.ResponseAsync;
            var headers = invoker.CapturedOptions!.Value.Headers;

            // Assert
            await Assert.That(headers!.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo(encodingName);
        }
    }

    [Test]
    public async ValueTask preserves_a_caller_requested_opt_out(CancellationToken cancellationToken) {
        // Arrange: the caller explicitly opted this call out of compression.
        var callerHeaders = new Metadata();
        callerHeaders.WithoutCompression();

        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new CompressionInterceptor(CompressionAlgorithm.Gzip));

        // Act
        using var call = subject.AsyncUnaryCall(
            TestMethod, null,
            new(headers: callerHeaders, cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert: the interceptor did not override the opt-out.
        await Assert.That(headers).IsSameReferenceAs(callerHeaders);
        await Assert.That(headers!.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("identity");
    }

    [Test]
    public async ValueTask overrides_a_caller_set_algorithm_that_differs_from_the_configured_one(CancellationToken cancellationToken) {
        // Arrange: something upstream already requested gzip, but the channel is configured
        // for Brotli — the channel's configured algorithm wins.
        var callerHeaders = new Metadata();
        callerHeaders.WithCompression(CompressionAlgorithm.Gzip);

        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new CompressionInterceptor(CompressionAlgorithm.Brotli));

        // Act
        using var call = subject.AsyncUnaryCall(
            TestMethod, null,
            new(headers: callerHeaders, cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert: exactly one header, now requesting the configured algorithm.
        await Assert.That(headers!.Count).IsEqualTo(1);
        await Assert.That(headers.GetValue(GrpcCompression.CompressionRequestAlgorithmHeader)).IsEqualTo("br");
    }

    sealed class CapturingCallInvoker : CallInvoker {
        public CallOptions? CapturedOptions { get; private set; }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
        ) {
            CapturedOptions = options;
            return new(
                Task.FromResult(default(TResponse)!), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => new(), () => { }
            );
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
        ) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options
        ) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
        ) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options
        ) => throw new NotSupportedException();
    }
}
