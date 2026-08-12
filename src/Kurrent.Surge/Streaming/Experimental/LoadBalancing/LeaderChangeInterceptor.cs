using System.Net;
using Grpc.Core;
using Grpc.Core.Interceptors;
using static System.Threading.Tasks.TaskContinuationOptions;

namespace Kurrent.Surge.Experimental.LoadBalancing;

/// <summary>
/// Detects the legacy not-leader failure on any call shape and announces the advertised
/// leader endpoint so the resolver can re-route without a gossip round-trip.
/// </summary>
/// <remarks>
/// The server reports the failure as an <see cref="RpcException"/> with trailers:
/// <code>
/// exception: not-leader
/// leader-endpoint-host: node2.example.com
/// leader-endpoint-port: 2113
/// </code>
/// </remarks>
sealed partial class LeaderChangeInterceptor : Interceptor {
    const TaskContinuationOptions ContinuationOptions = ExecuteSynchronously | OnlyOnFaulted;

    const string LegacyErrorCodeKey = "exception";
    const string NotLeaderErrorCode = "not-leader";
    const string LeaderHostKey      = "leader-endpoint-host";
    const string LeaderPortKey      = "leader-endpoint-port";

    public LeaderChangeInterceptor(Action<DnsEndPoint> announceLeader, ILogger logger) {
        InspectFailure = task => {
            if (task.Exception?.InnerException is not RpcException rex)
                return;

            if (rex.Trailers.GetValue(LegacyErrorCodeKey) != NotLeaderErrorCode)
                return;

            var host = rex.Trailers.GetValue(LeaderHostKey);
            if (host is null || !int.TryParse(rex.Trailers.GetValue(LeaderPortKey), out var port))
                return;

            LogLeaderChanged(logger, host, port);

            announceLeader(new(host, port));
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
