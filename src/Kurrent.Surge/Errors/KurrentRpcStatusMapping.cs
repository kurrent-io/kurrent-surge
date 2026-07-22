using Google.Rpc;
using Kurrent.GoogleRpc;
using Kurrent.Surge.Client.Errors;

namespace Kurrent.Surge.Client.Exceptions;

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
            details.ResourceInfo = Kurrent.Surge.Client.Errors.ResourceInfo.From(resourceInfo.ResourceType, resourceInfo.ResourceName);

        if (status.GetDetail<Google.Rpc.RetryInfo>() is { } retryInfo)
            details.RetryInfo = Kurrent.Surge.Client.Errors.RetryInfo.From(retryInfo.RetryDelay.ToTimeSpan());

        return details;
    }

    public static Exception? ToInnerException(Status status) =>
        status.GetDetail<DebugInfo>().ToException();
}
