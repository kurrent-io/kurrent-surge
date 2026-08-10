using System.Net;
using Google.Rpc;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Kurrent.Rpc;
using static System.Threading.Tasks.TaskContinuationOptions;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Detects the not-leader failure on any call shape and announces the advertised leader
/// endpoint so the resolver can re-route without a gossip round-trip.
/// </summary>
/// <remarks>
/// The server reports the failure with <c>StatusCode.FailedPrecondition</c> and a
/// <c>google.rpc.Status</c> in the <c>grpc-status-details-bin</c> trailer carrying an
/// Any-packed <see cref="NotLeaderNodeErrorDetails"/>:
/// <code>
/// Status { Code: FAILED_PRECONDITION, Details: [
///   ErrorInfo { Reason: "SERVER_ERROR_NOT_LEADER_NODE" },
///   NotLeaderNodeErrorDetails { CurrentLeader { Host: "node2", Port: 2113 } }
/// ]}
/// </code>
/// The detail message type, not the reason string, is the match key.
/// </remarks>
sealed partial class LeaderChangeInterceptor : Interceptor {
    const TaskContinuationOptions ContinuationOptions = ExecuteSynchronously | OnlyOnFaulted;

    public LeaderChangeInterceptor(Action<DnsEndPoint> announceLeader, ILogger logger) {
        InspectFailure = task => {
            if (task.Exception?.InnerException is not RpcException rex)
                return;

            if (rex.StatusCode != StatusCode.FailedPrecondition)
                return;

            var leader = rex.GetRpcStatus()?.GetDetail<NotLeaderNodeErrorDetails>()?.CurrentLeader;
            if (leader is null || leader.Host.Length == 0 || leader.Port is <= 0 or > ushort.MaxValue)
                return;

            LogLeaderChanged(logger, leader.Host, leader.Port);

            announceLeader(new(leader.Host, leader.Port));
        };
    }

    Action<Task> InspectFailure { get; }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    ) {
        var response = continuation(request, context);
        response.ResponseAsync.ContinueWith(InspectFailure, ContinuationOptions);
        return new(
            response.ResponseAsync, response.ResponseHeadersAsync, response.GetStatus,
            response.GetTrailers, response.Dispose
        );
    }

    public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        var response = continuation(context);
        response.ResponseAsync.ContinueWith(InspectFailure, ContinuationOptions);
        return new(
            new StreamWriter<TRequest>(response.RequestStream, InspectFailure),
            response.ResponseAsync, response.ResponseHeadersAsync,
            response.GetStatus, response.GetTrailers, response.Dispose
        );
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        var response = continuation(request, context);
        return new(
            new StreamReader<TResponse>(response.ResponseStream, InspectFailure),
            response.ResponseHeadersAsync, response.GetStatus,
            response.GetTrailers, response.Dispose
        );
    }

    public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation
    ) {
        var response = continuation(context);
        return new(
            new StreamWriter<TRequest>(response.RequestStream, InspectFailure),
            new StreamReader<TResponse>(response.ResponseStream, InspectFailure),
            response.ResponseHeadersAsync, response.GetStatus,
            response.GetTrailers, response.Dispose
        );
    }

    sealed class StreamWriter<T>(IClientStreamWriter<T> inner, Action<Task> onTaskFaulted) : IClientStreamWriter<T> {
        public WriteOptions? WriteOptions {
            get => inner.WriteOptions;
            set => inner.WriteOptions = value;
        }

        public Task CompleteAsync() {
            var task = inner.CompleteAsync();
            task.ContinueWith(onTaskFaulted, ContinuationOptions);
            return task;
        }

        public Task WriteAsync(T message) {
            var task = inner.WriteAsync(message);
            task.ContinueWith(onTaskFaulted, ContinuationOptions);
            return task;
        }
    }

    sealed class StreamReader<T>(IAsyncStreamReader<T> inner, Action<Task> onTaskFaulted) : IAsyncStreamReader<T> {
        public T Current => inner.Current;

        public Task<bool> MoveNext(CancellationToken cancellationToken) {
            var task = inner.MoveNext(cancellationToken);
            task.ContinueWith(onTaskFaulted, ContinuationOptions);
            return task;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Leader changed to {Host}:{Port}, refreshing cluster info...")]
    static partial void LogLeaderChanged(ILogger logger, string host, int port);
}
