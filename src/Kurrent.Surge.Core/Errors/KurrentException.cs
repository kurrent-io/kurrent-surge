using Kurrent.Client.Errors;

namespace Kurrent.Client.Exceptions;

/// <summary>
/// Core and base exception class for all Kurrent-related errors, encapsulating detailed error information.
/// <remarks>
/// This class serves as the foundation for more specific Kurrent exceptions, providing a consistent way to
/// include structured error details such as reason codes, metadata, retry information, resource context, and
/// authentication details.
/// </remarks>
/// </summary>
/// <remarks>
/// Pure contract type: it carries error data and nothing else. Constructing one from a wire
/// <c>Google.Rpc.Status</c> is a transport concern owned by the client — the source-generated
/// <c>KurrentRpcStatusMapping</c> translates a status into these BCL ctors, so Google.Rpc never
/// reaches the contract layer.
/// </remarks>
[PublicAPI]
public class KurrentException : Exception {
    public KurrentException(KurrentErrorDetails details, string reason, Exception? innerException = null)
        : base(reason, innerException) => ErrorDetails = details;

    public KurrentException(ErrorCode code, string reason, Exception? innerException = null)
        : this(new KurrentErrorDetails { ErrorCode = code }, reason, innerException) { }

    /// <summary>
    /// Detailed information about a Kurrent error, including reason, metadata, retry info, resource info, and auth info.
    /// </summary>
    public KurrentErrorDetails ErrorDetails { get; init; }

    public override string ToString() => $"[{ErrorDetails.ErrorCode}] {Message}";
}
