using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Rpc;
using Kurrent.Surge.Experimental.LoadBalancing;
using Microsoft.Extensions.Logging.Abstractions;
using Metadata = Grpc.Core.Metadata;
using RpcStatus = Google.Rpc.Status;

namespace Kurrent.Surge.Tests.Experimental.LoadBalancing;

public class LeaderChangeInterceptorTests {
    static readonly Marshaller<string> StringMarshaller = Marshallers.Create(_ => [], _ => "");

    static readonly Method<string, string> TestMethod =
        new(MethodType.Unary, "TestService", "TestMethod", StringMarshaller, StringMarshaller);

    [Test]
    public async ValueTask announces_leader_from_not_leader_error_details(CancellationToken cancellationToken) {
        // Arrange
        var expectedLeader = new ClusterEndpoint("node2.example.com", 2113);
        var failure        = NotLeaderFailure(expectedLeader.Host, expectedLeader.Port);

        var announced = new TaskCompletionSource<ClusterEndpoint>(TaskCreationOptions.RunContinuationsAsynchronously);
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
    public async ValueTask ignores_failures_with_other_status_codes(CancellationToken cancellationToken) {
        // Arrange: correct details but a non-FailedPrecondition status.
        var failure = new RpcStatus {
            Code    = (int)Code.Unavailable,
            Message = "unavailable",
            Details = {
                Any.Pack(new NotLeaderNodeErrorDetails {
                    CurrentLeader = new() { Host = "node2.example.com", Port = 2113 }
                })
            }
        }.ToRpcException();

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
    public async ValueTask ignores_failed_precondition_without_leader_details(CancellationToken cancellationToken) {
        // Arrange: right status code, wrong detail payload.
        var failure = new RpcStatus {
            Code    = (int)Code.FailedPrecondition,
            Message = "some other precondition failure",
            Details = {
                Any.Pack(new Google.Rpc.ErrorInfo { Reason = "SERVER_ERROR_STREAM_DELETED", Domain = "kurrentdb" })
            }
        }.ToRpcException();

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
    public async ValueTask ignores_leader_details_with_invalid_port(CancellationToken cancellationToken) {
        // Arrange
        var failure = NotLeaderFailure("node2.example.com", 0);

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

    static RpcException NotLeaderFailure(string host, int port) =>
        new RpcStatus {
            Code    = (int)Code.FailedPrecondition,
            Message = "The server is not the leader node and cannot handle the request.",
            Details = {
                Any.Pack(new Google.Rpc.ErrorInfo { Reason = "SERVER_ERROR_NOT_LEADER_NODE", Domain = "kurrentdb" }),
                Any.Pack(new NotLeaderNodeErrorDetails {
                    CurrentLeader = new() { Host = host, Port = port }
                })
            }
        }.ToRpcException();

    static CallInvoker CreateInvoker(RpcException failure, Action<ClusterEndpoint> announceLeader) =>
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
