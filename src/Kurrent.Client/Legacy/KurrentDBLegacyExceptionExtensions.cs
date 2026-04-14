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

        public T MapToResultError<T>(string legacyErrorCode, Func<RpcException, T> map) where T : IResultError {
            if (ex is not RpcException rex || !rex.TryGetLegacyErrorCode(out var code) || code != legacyErrorCode)
                throw new InvalidCastException($"Expected {nameof(RpcException)} with legacy error code {legacyErrorCode} but got {ex.GetType().Name} while mapping to {typeof(T).Name}.", ex);

            try {
                return map(rex);
            }
            catch (Exception mex) {
                throw new InvalidCastException($"Failed to map {nameof(RpcException)} with legacy error code {legacyErrorCode} to {typeof(T).Name}.", mex);
            }
        }
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
