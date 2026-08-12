using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Experimental.LoadBalancing;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

public class ClientHeadersInterceptorTests {
    const string ExcludedMethod = "MultiStreamAppendSession";

    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(_ => [], _ => "");

    static readonly IReadOnlyDictionary<string, string> IdentityHeaders = new Dictionary<string, string> {
        ["kurrentdb.client.name"] = "test-client",
        ["connection-name"]       = "test-connection"
    };

    [Test]
    public async ValueTask injects_identity_and_requires_leader_headers_creating_metadata_when_absent(CancellationToken cancellationToken) {
        // Arrange
        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders, requiresLeader: true, ExcludedMethod));

        // Act
        using var call = subject.AsyncUnaryCall(MethodNamed("Append"), null, new(cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers).IsNotNull();
        await Assert.That(headers!.GetValue("requires-leader")).IsEqualTo(bool.TrueString);
        await Assert.That(headers.GetValue("kurrentdb.client.name")).IsEqualTo("test-client");
        await Assert.That(headers.GetValue("connection-name")).IsEqualTo("test-connection");
    }

    [Test]
    public async ValueTask sends_requires_leader_false_when_leader_is_not_required(CancellationToken cancellationToken) {
        // Arrange
        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders, requiresLeader: false, ExcludedMethod));

        // Act
        using var call = subject.AsyncUnaryCall(MethodNamed("Append"), null, new(cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers!.GetValue("requires-leader")).IsEqualTo(bool.FalseString);
    }

    [Test]
    public async ValueTask preserves_caller_headers_when_metadata_is_present(CancellationToken cancellationToken) {
        // Arrange
        var callerHeaders = new Metadata { { "custom-header", "custom-value" } };

        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders, requiresLeader: true, ExcludedMethod));

        // Act
        using var call = subject.AsyncUnaryCall(
            MethodNamed("Append"), null,
            new(headers: callerHeaders, cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers).IsSameReferenceAs(callerHeaders);
        await Assert.That(headers!.GetValue("custom-header")).IsEqualTo("custom-value");
        await Assert.That(headers.GetValue("requires-leader")).IsEqualTo(bool.TrueString);
    }

    [Test]
    public async ValueTask skips_requires_leader_for_excluded_methods(CancellationToken cancellationToken) {
        // Arrange
        var invoker = new CapturingCallInvoker();
        var subject = invoker.Intercept(new ClientHeadersInterceptor(IdentityHeaders, requiresLeader: true, ExcludedMethod));

        // Act
        using var call = subject.AsyncUnaryCall(MethodNamed(ExcludedMethod), null, new(cancellationToken: cancellationToken), "request");
        await call.ResponseAsync;
        var headers = invoker.CapturedOptions!.Value.Headers;

        // Assert
        await Assert.That(headers!.GetValue("requires-leader")).IsNull();
        await Assert.That(headers.GetValue("kurrentdb.client.name")).IsEqualTo("test-client");
    }

    static Method<string, string> MethodNamed(string name) =>
        new(MethodType.Unary, "TestService", name, StringMarshaller, StringMarshaller);

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
