using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;

namespace Kurrent.Surge.Legacy;

static partial class LegacyExceptionExtensions {
    const string LegacyErrorCodeKey = "exception";

    extension(Exception ex) {
        public string? GetLegacyErrorCode() =>
            ex is RpcException rex
                ? rex.Trailers.FirstOrDefault(x => x.Key == LegacyErrorCodeKey)?.Value
                : null;

        public bool TryGetLegacyErrorCode([MaybeNullWhen(false)] out string errorCode) =>
            (errorCode = ex.GetLegacyErrorCode()) != null;
    }

    extension(RpcException rex) {
        public bool IsLegacyError(params string[] legacyErrorCodes) {
            var errorCode = rex.GetLegacyErrorCode();

            if (legacyErrorCodes.Any(code => code == errorCode))
                return true;

            // one of the hidden ones...
            // added explicitly to simplify the code
            // will fix this on the server soon
            if (legacyErrorCodes.Contains(LegacyErrorCodes.LogPositionNotFound)
             && IsHiddenLogPositionNotFound(rex))
                return true;

            return false;
        }
    }

    /// <summary>
    /// The server reports an unreadable log position as a bare Unknown status with the detail buried in
    /// the message, rather than as a legacy error code in the trailers.
    /// </summary>
    /// <remarks>
    /// The shape being matched:
    /// <code>
    /// StatusCode="Unknown", Detail="
    ///   Unexpected ReadAllResult: InvalidPosition >>
    ///   There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
    ///   Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
    /// "
    /// </code>
    /// Lives here rather than on <see cref="LogPositionNotFoundException"/> because it inspects a
    /// transport type, and that exception is now a plain contract type.
    /// </remarks>
    static bool IsHiddenLogPositionNotFound(RpcException rex) =>
        rex.StatusCode == StatusCode.Unknown &&
        rex.Message.Contains("InvalidPosition >>", StringComparison.OrdinalIgnoreCase);
}

static class LegacyErrorCodes {
    public const string StreamDeleted        = "stream-deleted";
    public const string WrongExpectedVersion = "wrong-expected-version";
    public const string LogPositionNotFound  = "invalid-log-position";
}