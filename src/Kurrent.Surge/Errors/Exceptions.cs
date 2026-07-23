// ReSharper disable CheckNamespace

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Google.Rpc;
using Grpc.Core;
using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

delegate Exception? OperationExceptionParser(Status status, ErrorInfo? errorInfo);

static class ExceptionExtensions {
    // Server errors can surface on any call, so their matchers are always tried. Everything else is
    // module-specific and passed by the call site that expects it.
    static readonly OperationExceptionParser[] ServerParsers = [
        LeaderForwardingTimeoutException.Match,
        ServerNotReadyException.Match,
        ServerOverloadedException.Match,
        NotLeaderNodeException.Match
    ];

    extension(Exception ex) {
        /// <summary>
        /// Translates a transport failure into the typed exception that describes it, or returns it
        /// unchanged when nothing matches.
        /// </summary>
        /// <param name="parsers">
        /// The matchers this call expects, built from each exception's <c>Match</c>. Server matchers are
        /// always tried after these.
        /// </param>
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

            // Try the call's parsers, then the server parsers, first non-null wins, else the original.
            // Generic gRPC status codes are deliberately NOT translated: a raw RpcException is the
            // ecosystem-standard transport-failure signal (Polly predicates, grpc-dotnet retry policies,
            // OTel conventions all key off RpcException.StatusCode). Typed exceptions are reserved for
            // domain errors the server describes.
            return parsers
                .Concat(ServerParsers)
                .Select(parse => parse(status, errorInfo))
                .SkipWhile(result => result is null)
                .DefaultIfEmpty(ex)
                .Select(result => (Exception)result!)
                .First();
        }

        /// <summary>
        /// Throws the typed exception that describes this transport failure, or rethrows it unchanged
        /// when nothing matches.
        /// </summary>
        /// <param name="parsers">
        /// The matchers this call expects. Server matchers are always tried after these.
        /// </param>
        /// <remarks>
        /// IMPORTANT!! The return type is just to satisfy the compiler. <br />
        /// Although this method indicates that it returns an exception, it never actually does.
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
