using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Kurrent.Client.Exceptions;

namespace Kurrent.Client.Legacy;

static partial class KurrentDBLegacyExceptionExtensions {
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
             && LogPositionNotFoundException.IsMatch(rex))
                return true;

            return false;
        }
    }
}
