using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Google.Rpc;
using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

/// <summary>
/// Maps a server error onto its typed exception. This is the transport boundary's dispatch table: it
/// answers "which exception is this status?" by looking the code up once, rather than by asking each
/// candidate type in turn.
/// </summary>
/// <remarks>
/// Keyed on the raw <c>ErrorInfo.Reason</c> string, never on a parsed <see cref="ErrorCode"/>.
/// <see cref="ErrorCode.From"/> is a lossy normalizer — it strips <c>Error</c> and <c>Exception</c>
/// case-insensitively before underscoring, so <c>SERVER_ERROR_NOT_READY</c> would silently become
/// <c>SERVER__NOT_READY</c> — and it throws on values that fail the pattern. Neither is acceptable on
/// untrusted input reached from inside a catch block. The keys come from the
/// <see cref="KurrentErrorCodes"/> constants, so the table stays pinned to the protocol, and the
/// comparison is case-insensitive to match the previous per-type dispatch.
/// </remarks>
static class KurrentRpcErrorMapper {
    delegate KurrentException Factory(Status status);

    // Construction still goes through each type's generated Status constructor, which applies
    // KurrentRpcStatusMapping and the type's AfterParseRpcStatus unpacker. Only dispatch moved here;
    // the unpackers fold into these entries when the types become plain.
    static readonly FrozenDictionary<string, Factory> Factories = new Dictionary<string, Factory>(StringComparer.OrdinalIgnoreCase) {
        [KurrentErrorCodes.Streams.StreamNotFound]                   = static s => new StreamNotFoundException(s),
        [KurrentErrorCodes.Streams.StreamAlreadyExists]              = static s => new StreamAlreadyExistsException(s),
        [KurrentErrorCodes.Streams.StreamDeleted]                    = static s => new StreamDeletedException(s),
        [KurrentErrorCodes.Streams.StreamTombstoned]                 = static s => new StreamTombstonedException(s),
        [KurrentErrorCodes.Streams.StreamRevisionConflict]           = static s => new StreamRevisionConflictException(s),
        [KurrentErrorCodes.Streams.StreamAlreadyInAppendSession]     = static s => new StreamAlreadyInAppendSessionException(s),
        [KurrentErrorCodes.Streams.AppendSessionNoRequests]          = static s => new AppendSessionNoRequestsException(s),
        [KurrentErrorCodes.Streams.AppendConsistencyViolation]       = static s => new AppendConsistencyViolationException(s),
        [KurrentErrorCodes.Streams.MaxAppendRecordSizeExceeded]      = static s => new MaxAppendRecordSizeExceededException(s),
        [KurrentErrorCodes.Streams.MaxAppendTransactionSizeExceeded] = static s => new MaxAppendTransactionSizeExceededException(s),
        [KurrentErrorCodes.Streams.LogPositionNotFound]              = static s => new LogPositionNotFoundException(s),

        [KurrentErrorCodes.Registry.SchemaNotFound]      = static s => new SchemaNotFoundException(s),
        [KurrentErrorCodes.Registry.SchemaAlreadyExists] = static s => new SchemaAlreadyExistsException(s),
        [KurrentErrorCodes.Registry.SchemaDeleted]       = static s => new SchemaDeletedException(s),

        [KurrentErrorCodes.Server.NotLeaderNode]           = static s => new NotLeaderNodeException(s),
        [KurrentErrorCodes.Server.ServerNotReady]          = static s => new ServerNotReadyException(s),
        [KurrentErrorCodes.Server.ServerOverloaded]        = static s => new ServerOverloadedException(s),
        [KurrentErrorCodes.Server.LeaderForwardingTimeout] = static s => new LeaderForwardingTimeoutException(s),

        [KurrentErrorCodes.Connectors.ConnectorNotFound]      = static s => new ConnectorNotFoundException(s),
        [KurrentErrorCodes.Connectors.ConnectorAlreadyExists] = static s => new ConnectorAlreadyExistsException(s),
        [KurrentErrorCodes.Connectors.ConnectorDeleted]       = static s => new ConnectorDeletedException(s)
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this error code has a typed exception. The completeness guarantee the per-type dispatch
    /// never offered: a test walks each protocol enum through this.
    /// </summary>
    internal static bool Handles(ErrorCode code) => Factories.ContainsKey(code.Value);

    internal static bool TryMap(Status status, ErrorInfo? errorInfo, [NotNullWhen(true)] out KurrentException? exception) {
        exception = errorInfo?.Reason is { Length: > 0 } reason && Factories.TryGetValue(reason, out var factory)
            ? factory(status)
            : null;

        return exception is not null;
    }
}
