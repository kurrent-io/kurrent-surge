using Google.Rpc;
using Humanizer;
using Kurrent.GoogleRpc;

namespace Kurrent.Client.Errors;

[PublicAPI]
public static class ErrorCodeExtensions {
    extension(ErrorCode errorCode) {
        public bool MatchesRpcStatusReason(Status status, ErrorInfo? errorInfo = null) =>
            errorCode.Value.Equals(errorInfo?.Reason ?? "", StringComparison.OrdinalIgnoreCase) ||
            status.GetGrpcStatusCode().ToString().Underscore().ToUpperInvariant().Equals(errorCode.Value, StringComparison.OrdinalIgnoreCase);
    }
}
