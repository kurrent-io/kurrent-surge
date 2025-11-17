// ReSharper disable CheckNamespace

using Google.Rpc;
using Google.Rpc.Exceptions;
using Grpc.Core;
using Kurrent.Client.Errors;
using Kurrent.Rpc;
using Kurrent.Variant;
using Status = Google.Rpc.Status;

namespace Kurrent.Client.Exceptions;

[KurrentOperationError(ServerError.LeaderForwardingTimeout)]
public partial class LeaderForwardingTimeoutException;

[KurrentOperationError(ServerError.ServerNotReady)]
public partial class ServerNotReadyException;

[KurrentOperationError(ServerError.ServerOverloaded)]
public partial class ServerOverloadedException;

[KurrentOperationError(ServerError.NotLeaderNode)]
public partial class NotLeaderNodeException {
    public NodeInfo CurrentLeader { get; private set; } = NodeInfo.None;

    public void AfterParseRpcStatus(Status status, NotLeaderNodeException self) {
        var details = status.GetDetail<NotLeaderNodeErrorDetails>()!;
        self.CurrentLeader = NodeInfo.From(
            details.CurrentLeader.Host,
            details.CurrentLeader.Port,
            details.CurrentLeader.NodeId);
    }
}

delegate dynamic? OperationExceptionParser(Status status, ErrorInfo? errorInfo);

static class ExceptionExtensions {
    static readonly OperationExceptionParser[] GoogleParsers = [
        static (s, e) => CancelledException.From(s, e),
        static (s, e) => UnknownException.From(s, e),
        static (s, e) => InvalidArgumentException.From(s, e),
        static (s, e) => DeadlineExceededException.From(s, e),
        static (s, e) => NotFoundException.From(s, e),
        static (s, e) => AlreadyExistsException.From(s, e),
        static (s, e) => PermissionDeniedException.From(s, e),
        static (s, e) => UnauthenticatedException.From(s, e),
        static (s, e) => ResourceExhaustedException.From(s, e),
        static (s, e) => FailedPreconditionException.From(s, e),
        static (s, e) => AbortedException.From(s, e),
        static (s, e) => OutOfRangeException.From(s, e),
        static (s, e) => UnimplementedException.From(s, e),
        static (s, e) => InternalException.From(s, e),
        static (s, e) => UnavailableException.From(s, e),
        static (s, e) => DataLossException.From(s, e)
    ];

    static readonly OperationExceptionParser[] ServerParsers = [
        static (s, e) => LeaderForwardingTimeoutException.From(s, e),
        static (s, e) => ServerNotReadyException.From(s, e),
        static (s, e) => ServerOverloadedException.From(s, e),
        static (s, e) => NotLeaderNodeException.From(s, e)
    ];

    extension(Exception ex) {
        /// <summary>
        /// Dangerously converts an Exception into a variant error result by trying to parse it using the provided parsers,
        /// followed by the built-in server and standard google parsers.
        /// </summary>
        public TError ToExceptionalErrorResult<TError>(params OperationExceptionParser[] parsers) where TError : class, IVariantException {
            // pass through KurrentException as is (just in case)
            if (ex is KurrentException kex)
                return (dynamic)kex;

            // fast fail for non-RpcException
            if (ex is not RpcException rex)
                return (dynamic)ex;

            // extract status and error info
            var status    = rex.GetRpcStatus();
            var errorInfo = status?.GetDetail<ErrorInfo>();

            // create status fallback
            status ??= new Status {
                Code    = (int)rex.StatusCode,
                Message = rex.Message
            };

            // try to parse using the provided parsers, then server parsers, then google parsers
            // and finally fallback to the original exception
            return parsers
                .Concat(ServerParsers)
                .Concat(GoogleParsers)
                .Select(parse => parse(status, errorInfo))
                .SkipWhile(result => result is null)
                .DefaultIfEmpty(ex)
                .Select(ex => (TError)ex!)
                .First();
        }
    }
}
