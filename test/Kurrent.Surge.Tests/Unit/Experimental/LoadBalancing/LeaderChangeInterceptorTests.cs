using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Surge.Experimental.LoadBalancing;
using Microsoft.Extensions.Logging.Abstractions;
using Metadata = Grpc.Core.Metadata;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

public class LeaderChangeInterceptorTests {
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(_ => [], _ => "");

    static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "TestService", "TestMethod", StringMarshaller, StringMarshaller);

    [Test]
    public async ValueTask announces_leader_from_not_leader_trailers(CancellationToken cancellationToken) {
        // Arrange
        var expectedLeader = new DnsEndPoint("node2.example.com", 2113);

        var failure = new RpcException(
            new(StatusCode.FailedPrecondition, "not leader"),
            new Metadata {
                { "exception", "not-leader" },
                { "leader-endpoint-host", expectedLeader.Host },
                { "leader-endpoint-port", expectedLeader.Port.ToString() }
            }
        );

        var announced = new TaskCompletionSource<DnsEndPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker   = CreateInvoker(failure, announced.SetResult);

        // Act
        using var call = invoker.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
        try {
            await call.ResponseAsync;
        }
        catch (RpcException) {
        }

        var leader = await announced.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        // Assert
        await Assert.That(leader).IsEqualTo(expectedLeader);
    }

    [Test]
    public async ValueTask ignores_failures_without_the_not_leader_code(CancellationToken cancellationToken) {
        // Arrange
        var failure = new RpcException(
            new(StatusCode.FailedPrecondition, "some other failure"),
            new Metadata { { "exception", "stream-deleted" } }
        );

        var announcedLeader = false;
        var invoker         = CreateInvoker(failure, _ => announcedLeader = true);

        // Act
        using var call = invoker.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
        try {
            await call.ResponseAsync;
        }
        catch (RpcException) {
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        // Assert
        await Assert.That(announcedLeader).IsFalse();
    }

    [Test]
    public async ValueTask ignores_not_leader_failures_with_malformed_endpoint_trailers(CancellationToken cancellationToken) {
        // Arrange
        var failure = new RpcException(
            new(StatusCode.FailedPrecondition, "not leader"),
            new Metadata {
                { "exception", "not-leader" },
                { "leader-endpoint-host", "node2.example.com" },
                { "leader-endpoint-port", "not-a-port" }
            }
        );

        var announcedLeader = false;
        var invoker         = CreateInvoker(failure, _ => announcedLeader = true);

        // Act
        using var call = invoker.AsyncUnaryCall(TestMethod, null, new(cancellationToken: cancellationToken), "request");
        try {
            await call.ResponseAsync;
        }
        catch (RpcException) {
        }

        await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);

        // Assert
        await Assert.That(announcedLeader).IsFalse();
    }

    static CallInvoker CreateInvoker(RpcException failure, Action<DnsEndPoint> announceLeader) =>
        new FaultyCallInvoker(failure).Intercept(new LeaderChangeInterceptor(announceLeader, NullLogger.Instance));

    sealed class FaultyCallInvoker(RpcException failure) : CallInvoker {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
        ) =>
            new(
                Task.FromException<TResponse>(failure), Task.FromResult(new Metadata()),
                () => failure.Status, () => failure.Trailers, () => { }
            );

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request
        ) => throw failure;

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
