using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Google.Rpc;
using Kurrent.Rpc;
using Kurrent.Surge.Client.Errors;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

using RegistryErrors = KurrentDB.Protocol.V2.Registry.Errors;
using StreamsErrors = KurrentDB.Protocol.V2.Streams.Errors;
using Status = Google.Rpc.Status;

namespace Kurrent.Surge.Client.Exceptions;

/// <summary>
/// Maps a server error onto its typed exception. This is the transport boundary's dispatch table: it
/// answers "which exception is this status?" with one lookup, rather than by asking each candidate type
/// in turn, and it owns the deserialization of every typed error detail.
/// </summary>
/// <remarks>
/// Keyed on the raw <c>ErrorInfo.Reason</c> string, and each entry carries its own
/// <see cref="ErrorCode"/> so a matched reason is never parsed back into one.
/// <see cref="ErrorCode.From"/> is a lossy normalizer — it strips <c>Error</c> and <c>Exception</c>
/// case-insensitively before underscoring, so <c>SERVER_ERROR_NOT_READY</c> would silently become
/// <c>SERVER__NOT_READY</c> — and it throws on values that fail the pattern. Neither is acceptable on
/// untrusted input reached from inside a catch block. The keys come from the
/// <see cref="KurrentErrorCodes"/> constants, so the table stays pinned to the protocol, and the
/// comparison is case-insensitive to match the dispatch this replaced.
/// </remarks>
static class KurrentRpcErrorMapper {
    delegate KurrentException Factory(KurrentErrorDetails details, string reason, Exception? innerException, Status status);

    readonly record struct ErrorMapping(ErrorCode Code, Factory Create);

    static readonly FrozenDictionary<string, ErrorMapping> Mappings = new ErrorMapping[] {
        #region ->> Streams <<-

        // No wire payload: the code alone is the whole error.
        new(KurrentErrorCodes.Streams.StreamNotFound,
            static (d, m, i, _) => new StreamNotFoundException(d, m, i)),

        new(KurrentErrorCodes.Streams.StreamAlreadyExists,
            static (d, m, i, _) => new StreamAlreadyExistsException(d, m, i)),

        new(KurrentErrorCodes.Streams.StreamDeleted,
            static (d, m, i, _) => new StreamDeletedException(d, m, i)),

        new(KurrentErrorCodes.Streams.StreamTombstoned,
            static (d, m, i, _) => new StreamTombstonedException(d, m, i)),

        new(KurrentErrorCodes.Streams.StreamAlreadyInAppendSession,
            static (d, m, i, _) => new StreamAlreadyInAppendSessionException(d, m, i)),

        new(KurrentErrorCodes.Streams.AppendSessionNoRequests,
            static (d, m, i, _) => new AppendSessionNoRequestsException(d, m, i)),

        new(KurrentErrorCodes.Streams.LogPositionNotFound,
            static (d, m, i, _) => new LogPositionNotFoundException(d, m, i)),

        // Carries a typed detail message, so unpacking it is deserialization and belongs here. Every
        // unpacker treats an absent detail as "no payload" rather than dereferencing it: this runs from
        // inside a catch block, where a null-reference would replace the server's error with our own.
        new(KurrentErrorCodes.Streams.StreamRevisionConflict,
            static (d, m, i, status) => {
                var detail = status.GetDetail<StreamsErrors.StreamRevisionConflictErrorDetails>();

                return new StreamRevisionConflictException(d, m, i) {
                    Stream           = detail?.Stream ?? StreamName.Unset,
                    ExpectedRevision = ExpectedStreamState.FromWireValue(detail?.ExpectedRevision ?? -2),
                    ActualRevision   = ActualStreamState.FromWireValue(detail?.ActualRevision ?? -1)
                };
            }),

        new(KurrentErrorCodes.Streams.AppendConsistencyViolation,
            static (d, m, i, status) => new AppendConsistencyViolationException(d, m, i) {
                Violations = ToViolations(status.GetDetail<StreamsErrors.AppendConsistencyViolationErrorDetails>())
            }),

        new(KurrentErrorCodes.Streams.MaxAppendRecordSizeExceeded,
            static (d, m, i, status) => {
                var detail = status.GetDetail<StreamsErrors.AppendRecordSizeExceededErrorDetails>();

                return new MaxAppendRecordSizeExceededException(d, m, i) {
                    RecordId         = Guid.TryParse(detail?.RecordId, out var recordId) ? recordId : Guid.Empty,
                    RecordActualSize = detail?.Size ?? 0,
                    RecordMaxSize    = detail?.MaxSize ?? 0
                };
            }),

        new(KurrentErrorCodes.Streams.MaxAppendTransactionSizeExceeded,
            static (d, m, i, status) => {
                var detail = status.GetDetail<StreamsErrors.AppendTransactionSizeExceededErrorDetails>();

                return new MaxAppendTransactionSizeExceededException(d, m, i) {
                    TransactionActualSize = detail?.Size ?? 0,
                    TransactionMaxSize    = detail?.MaxSize ?? 0
                };
            }),

        #endregion

        #region ->> Registry <<-

        new(KurrentErrorCodes.Registry.SchemaNotFound,
            static (d, m, i, status) => new SchemaNotFoundException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaNotFoundErrorDetails>()?.Schema)
            }),

        new(KurrentErrorCodes.Registry.SchemaAlreadyExists,
            static (d, m, i, status) => new SchemaAlreadyExistsException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaAlreadyExistsErrorDetails>()?.Schema)
            }),

        new(KurrentErrorCodes.Registry.SchemaDeleted,
            static (d, m, i, status) => new SchemaDeletedException(d, m, i) {
                Schema = ToSchemaName(status.GetDetail<RegistryErrors.SchemaDeletedErrorDetails>()?.Schema)
            }),

        #endregion

        #region ->> Server <<-

        new(KurrentErrorCodes.Server.ServerNotReady,
            static (d, m, i, _) => new ServerNotReadyException(d, m, i)),

        new(KurrentErrorCodes.Server.ServerOverloaded,
            static (d, m, i, _) => new ServerOverloadedException(d, m, i)),

        new(KurrentErrorCodes.Server.LeaderForwardingTimeout,
            static (d, m, i, _) => new LeaderForwardingTimeoutException(d, m, i)),

        new(KurrentErrorCodes.Server.NotLeaderNode,
            static (d, m, i, status) => new NotLeaderNodeException(d, m, i) {
                CurrentLeader = ToNodeInfo(status.GetDetail<NotLeaderNodeErrorDetails>()?.CurrentLeader)
            }),

        #endregion

        #region ->> Connectors <<-

        new(KurrentErrorCodes.Connectors.ConnectorNotFound,
            static (d, m, i, _) => new ConnectorNotFoundException(d, m, i)),

        new(KurrentErrorCodes.Connectors.ConnectorAlreadyExists,
            static (d, m, i, _) => new ConnectorAlreadyExistsException(d, m, i)),

        new(KurrentErrorCodes.Connectors.ConnectorDeleted,
            static (d, m, i, _) => new ConnectorDeletedException(d, m, i))

        #endregion
    }.ToFrozenDictionary(x => x.Code.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this error code has a typed exception. The completeness guarantee the per-type dispatch
    /// never offered: a test walks each protocol enum through this.
    /// </summary>
    internal static bool Handles(ErrorCode code) => Mappings.ContainsKey(code.Value);

    internal static bool TryMap(Status status, ErrorInfo? errorInfo, [NotNullWhen(true)] out KurrentException? exception) {
        exception = errorInfo?.Reason is { Length: > 0 } reason && Mappings.TryGetValue(reason, out var mapping)
            ? mapping.Create(
                KurrentRpcStatusMapping.ToErrorDetails(mapping.Code, status),
                status.Message,
                KurrentRpcStatusMapping.ToInnerException(status),
                status)
            : null;

        return exception is not null;
    }

    static SchemaName? ToSchemaName(string? schema) =>
        schema is { Length: > 0 } ? SchemaName.From(schema) : null;

    /// <summary>
    /// The leader hint is best-effort: <c>node_id</c> is optional on the wire and the details message
    /// itself may be absent, so an unusable hint degrades to <see cref="NodeInfo.None"/> rather than
    /// turning a retriable not-leader error into a crash.
    /// </summary>
    static NodeInfo ToNodeInfo(NotLeaderNodeErrorDetails.Types.NodeInfo? leader) =>
        leader is { Host.Length: > 0 }
            ? NodeInfo.From(
                new DnsEndPoint(leader.Host, leader.Port),
                Guid.TryParse(leader.NodeId, out var nodeId) ? nodeId : Guid.Empty)
            : NodeInfo.None;

    static IReadOnlyList<ConsistencyViolation> ToViolations(StreamsErrors.AppendConsistencyViolationErrorDetails? details) =>
        details is null
            ? []
            : [
                .. details.Violations.Select(v => {
                    ConsistencyViolation violation = v.TypeCase switch {
                        StreamsErrors.ConsistencyViolation.TypeOneofCase.StreamState => new StreamStateViolation {
                            Stream        = v.StreamState.Stream,
                            ExpectedState = ExpectedStreamState.FromWireValue(v.StreamState.ExpectedState),
                            ActualState   = ActualStreamState.FromWireValue(v.StreamState.ActualState)
                        },
                        // StreamsErrors.ConsistencyViolation.TypeOneofCase.QueryPredicate => new QueryPredicateViolation {
                        //     Message = v.QueryPredicate.Message
                        // },
                        _ => throw new ArgumentOutOfRangeException($"Unknown consistency check error type: {v.TypeCase}")
                    };

                    return violation with { CheckIndex = v.CheckIndex };
                })
            ];
}
