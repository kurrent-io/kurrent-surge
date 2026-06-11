using System.Diagnostics.CodeAnalysis;
using Google.Rpc;
using Kurrent.Client.Errors;
using Kurrent.GoogleRpc;
using RetryInfo = Kurrent.Client.Errors.RetryInfo;
using ResourceInfo = Kurrent.Client.Errors.ResourceInfo;
using Status = Google.Rpc.Status;

namespace Kurrent.Client.Exceptions;

/// <summary>
/// Core and base exception class for all Kurrent-related errors, encapsulating detailed error information.
/// <remarks>
/// This class serves as the foundation for more specific Kurrent exceptions, providing a consistent way to
/// include structured error details such as reason codes, metadata, retry information, resource context, and
/// authentication details.
/// </remarks>
/// </summary>
[PublicAPI]
public class KurrentException : Exception {
    public KurrentException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(reason, innerException) => ErrorDetails = details;

    public KurrentException(ErrorCode code, string reason, Exception? innerException = null)
        : this(new KurrentErrorDetails { ErrorCode = code }, reason, innerException) { }

    protected internal KurrentException(ErrorCode code, Status status) : base(status.Message, status.GetDetail<DebugInfo>().ToException()) {
        var details = new KurrentErrorDetails { ErrorCode = code };

        if (status.GetDetail<Google.Rpc.ResourceInfo>() is { ResourceType.Length: > 0, ResourceName.Length: > 0 } resourceInfo)
            details.ResourceInfo = ResourceInfo.From(resourceInfo.ResourceType, resourceInfo.ResourceName);

        if (status.GetDetail<Google.Rpc.RetryInfo>() is { } retryInfo)
            details.RetryInfo = RetryInfo.From(retryInfo.RetryDelay.ToTimeSpan());

        ErrorDetails = details;
    }

    /// <summary>
    /// Detailed information about a Kurrent error, including reason, metadata, retry info, resource info, and auth info.
    /// </summary>
    public KurrentErrorDetails ErrorDetails { get; init; }

    public override string ToString() => $"[{ErrorDetails.ErrorCode}] {Message}";
}

/// <summary>
/// Interface for Kurrent exceptions that can be parsed from gRPC Status messages.
/// </summary>
public interface IKurrentOperationException<T> where T : KurrentException {
    /// <summary>
    /// The associated error code for this exception type.
    /// It will match against RPC statuses to determine if this exception is the correct one to instantiate.
    /// </summary>
    static abstract ErrorCode Code { get; }

    /// <summary>
    /// Hook called after parsing the RPC status into the exception instance.
    /// Allows for post-processing or additional initialization.
    /// </summary>
    void AfterParseRpcStatus(Status status, T self) { }

    /// <summary>
    /// Attempts to parse the given gRPC Status into an instance of this exception type.
    /// </summary>
    static abstract bool TryParseRpcStatus(Status status, ErrorInfo? errorInfo, [MaybeNullWhen(false)] out T ex);

    /// <summary>
    /// Attempts to parse the given gRPC Status into an instance of this exception type.
    /// </summary>
    static abstract T? From(Status status, ErrorInfo? errorInfo = null);
}
