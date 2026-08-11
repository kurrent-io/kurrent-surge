using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Grpc.Interceptors;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Internal;

public class ClientHeadersInterceptorTests {
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(_ => [], _ => "");

    static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "TestService", "Append", StringMarshaller, StringMarshaller);

    static readonly IReadOnlyDictionary<string, string> IdentityHeaders = new Dictionary<string, string> {
        ["kurrentdb.client.name"] = "test-client",
        ["connection-name"]       = "test-connection"
    };

    [Test]
    public async ValueTask injects_identity_headers_creating_metadata_when_absent(CancellationToken cancellationToken) {
        // Arrange
        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders));

        // Act
        using var call = subject.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue("kurrentdb.client.name")).IsEqualTo("test-client");
        await Assert.That(headers.GetValue("connection-name")).IsEqualTo("test-connection");
        await Assert.That(headers.GetValue("requires-leader")).IsNull();
    }

    [Test]
    public async ValueTask preserves_caller_headers_when_metadata_is_present(CancellationToken cancellationToken) {
        // Arrange
        var callerHeaders = new Metadata { { "custom-header", "custom-value" } };

        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders));

        // Act
        using var call = subject.AsyncUnaryCall(
            TestMethod, null,
            new(headers: callerHeaders, cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers).IsSameReferenceAs(callerHeaders);
        await Assert.That(headers!.GetValue("custom-header")).IsEqualTo("custom-value");
        await Assert.That(headers.GetValue("kurrentdb.client.name")).IsEqualTo("test-client");
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
