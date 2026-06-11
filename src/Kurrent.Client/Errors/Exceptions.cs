// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Google.Rpc;
using Grpc.Core;
using Kurrent.Client.Errors;
using Kurrent.Rpc;
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
    /// <summary>
    /// Creates a fallback parser that matches on gRPC StatusCode when the server
    /// does not send structured ErrorInfo. Remove individual usages when the
    /// server sends ErrorInfo for that module.
    /// </summary>
    extension(StatusCode statusCode) {
        internal OperationExceptionParser MapTo(Exception exception) =>
            (s, _) => (StatusCode)s.Code == statusCode ? (dynamic?)exception : null;
    }

    static readonly OperationExceptionParser[] ServerParsers = [
        static (s, e) => LeaderForwardingTimeoutException.From(s, e),
        static (s, e) => ServerNotReadyException.From(s, e),
        static (s, e) => ServerOverloadedException.From(s, e),
        static (s, e) => NotLeaderNodeException.From(s, e)
    ];

    extension(Exception ex) {
        [StackTraceHidden]
        internal Exception ToException(params OperationExceptionParser[] parsers) {
            // pass through KurrentException as is (just in case)
            if (ex is KurrentException kex)
                return kex;

            // fast fail for non-RpcException
            if (ex is not RpcException rex)
                return ex;

            // extract status and error info
            var status    = rex.GetRpcStatus();
            var errorInfo = status?.GetDetail<ErrorInfo>();

            // create status fallback
            status ??= new() {
                Code    = (int)rex.StatusCode,
                Message = rex.Message
            };

            // try to parse using the provided parsers, then server parsers, and finally
            // fall back to the original exception. Generic gRPC status codes are deliberately
            // NOT translated: a raw RpcException is the ecosystem-standard transport-failure
            // signal (Polly predicates, grpc-dotnet retry policies, OTel conventions all key
            // off RpcException.StatusCode). Typed exceptions are reserved for domain errors
            // the server describes via ErrorInfo.
            return parsers
                .Concat(ServerParsers)
                .Select(parse => parse(status, errorInfo))
                .SkipWhile(result => result is null)
                .DefaultIfEmpty(ex)
                .Select(ex => (Exception)ex!)
                .First();
        }

        /// <summary>
        /// Converts the current exception into a well-known exception by attempting
        /// to parse it using the provided operation exception parsers. If no parser
        /// successfully handles the exception, the original exception is returned.
        /// </summary>
        /// <param name="parsers">
        /// An array of operation exception parsers to attempt for converting the current exception.
        /// These parsers are applied before any default or built-in parsers.
        /// </param>
        /// <remarks>
        /// IMPORTANT!! The return type is just to satify the compiler. <br />
        /// Although this method indicates that it return an exception, it never actually does it.
        /// It simply captures the exception and re-throws it.
        /// </remarks>
        [StackTraceHidden]
        public Exception AsWellKnownException(params OperationExceptionParser[] parsers) {
            var edi = ExceptionDispatchInfo.Capture(ex.ToException(parsers));
            edi.Throw();
            return ex; // unreachable - only for compiler
        }
    }
}
