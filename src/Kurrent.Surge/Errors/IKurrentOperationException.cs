using System.Diagnostics.CodeAnalysis;
using Google.Rpc;
using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

/// <summary>
/// Interface for Kurrent exceptions that can be parsed from gRPC Status messages. This is the
/// transport-side parse protocol — it lives in the client, not the contract, because it speaks
/// Google.Rpc. The source generator implements it for every <c>[KurrentOperationError]</c> type.
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
