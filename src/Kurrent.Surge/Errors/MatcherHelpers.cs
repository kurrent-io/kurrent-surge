using Google.Rpc;
using Grpc.Core;
using Kurrent.Toolkit.GoogleRpc;
using Kurrent.Surge.Client.Errors;
using Kurrent.Surge.Schema;

using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

static class MatcherHelpers {
    // The server named the error through ErrorInfo: match on the code, then build with the wire details
    // attached. The build callback adds any typed-detail unpack.
    public static T? OnReason<T>(Status status, ErrorInfo? errorInfo, ErrorCode code, Func<KurrentErrorDetails, string, Exception?, T> build)
        where T : KurrentException =>
        code.MatchesRpcStatusReason(status, errorInfo)
            ? build(KurrentRpcStatusMapping.ToErrorDetails(code, status), status.Message, KurrentRpcStatusMapping.ToInnerException(status))
            : null;

    // This operation maps a bare gRPC status code to one domain exception the caller already knows how
    // to build, for a module that does not send ErrorInfo yet.
    public static OperationExceptionParser OnStatusCode(StatusCode statusCode, Func<Exception> build) =>
        (status, _) => (StatusCode)status.Code == statusCode ? build() : null;

    public static OperationExceptionParser OnStatus(Predicate<Status> predicate, Func<Exception> build) =>
        (status, _) => predicate(status) ? build() : null;
    
    // Last resort for a module that sends neither ErrorInfo nor a typed detail — only a status message.
    // The raw message shapes matched are documented at the call site.
    public static OperationExceptionParser OnMessage(string contains, Func<Exception> build) =>
        (status, _) => status.Message.Contains(contains, StringComparison.OrdinalIgnoreCase) ? build() : null;
}

[PublicAPI]
public static class ErrorCodeExtensions {
    extension(ErrorCode errorCode) {
        public bool MatchesRpcStatusReason(Status status, ErrorInfo? errorInfo = null) =>
            errorCode.Value.Equals(errorInfo?.Reason ?? "", StringComparison.OrdinalIgnoreCase) ||
            status.GetGrpcStatusCode().ToString().Underscore().ToUpperInvariant().Equals(errorCode.Value, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The transport boundary: maps a wire <see cref="Status"/> onto the BCL error shape the contract-layer
/// <see cref="KurrentException"/> carries.
/// </summary>
/// <remarks>
/// Lives in the client, never in the contract, so <c>Google.Rpc</c> stops at this file.
/// <c>Google.Rpc.ResourceInfo</c> and <c>Google.Rpc.RetryInfo</c> are fully qualified to disambiguate
/// them from the <see cref="Kurrent.Surge.Client.Errors"/> domain types of the same name.
/// </remarks>
static class KurrentRpcStatusMapping {
    public static KurrentErrorDetails ToErrorDetails(ErrorCode code, Status status) {
        var details = new KurrentErrorDetails { ErrorCode = code };

        if (status.GetDetail<Google.Rpc.ResourceInfo>() is { ResourceType.Length: > 0, ResourceName.Length: > 0 } resourceInfo)
            details.ResourceInfo = Errors.ResourceInfo.From(resourceInfo.ResourceType, resourceInfo.ResourceName);

        if (status.GetDetail<Google.Rpc.RetryInfo>() is { } retryInfo)
            details.RetryInfo = Errors.RetryInfo.From(retryInfo.RetryDelay.ToTimeSpan());

        return details;
    }

    public static Exception? ToInnerException(Status status) =>
        status.GetDetail<DebugInfo>().ToException();
}
