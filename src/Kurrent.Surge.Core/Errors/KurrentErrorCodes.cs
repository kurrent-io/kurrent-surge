using System.Collections.Frozen;

namespace Kurrent;

/// <summary>
/// The server error-code vocabulary, grouped by the protocol module that defines it.
/// </summary>
/// <remarks>
/// These are plain constants rather than the compiled protocol enums so the contract layer can name an
/// error without referencing the transport. Each group is pinned to its protocol enum by a test in the
/// client's test project, where those enums are visible; the pin fails the day a code is added or renamed
/// in a proto. Values are the UPPER_SNAKE_CASE form the server sends as <c>ErrorInfo.Reason</c>.
/// </remarks>
[PublicAPI]
public static class KurrentErrorCodes {
    /// <summary>
    /// Errors raised by the streams module. Pinned to <c>KurrentDB.Protocol.V2.Streams.Errors.StreamsError</c>.
    /// </summary>
    public static class Streams {
        public static readonly ErrorCode StreamNotFound                   = ErrorCode.From("STREAM_NOT_FOUND");
        public static readonly ErrorCode StreamAlreadyExists              = ErrorCode.From("STREAM_ALREADY_EXISTS");
        public static readonly ErrorCode StreamDeleted                    = ErrorCode.From("STREAM_DELETED");
        public static readonly ErrorCode StreamTombstoned                 = ErrorCode.From("STREAM_TOMBSTONED");
        public static readonly ErrorCode StreamRevisionConflict           = ErrorCode.From("STREAM_REVISION_CONFLICT");
        public static readonly ErrorCode StreamRevisionNotFound           = ErrorCode.From("STREAM_REVISION_NOT_FOUND");
        public static readonly ErrorCode StreamAlreadyInAppendSession     = ErrorCode.From("STREAM_ALREADY_IN_APPEND_SESSION");
        public static readonly ErrorCode AppendSessionNoRequests          = ErrorCode.From("APPEND_SESSION_NO_REQUESTS");
        public static readonly ErrorCode AppendConsistencyViolation       = ErrorCode.From("APPEND_CONSISTENCY_VIOLATION");
        public static readonly ErrorCode MaxAppendRecordSizeExceeded      = ErrorCode.From("MAX_APPEND_RECORD_SIZE_EXCEEDED");
        public static readonly ErrorCode MaxAppendTransactionSizeExceeded = ErrorCode.From("MAX_APPEND_TRANSACTION_SIZE_EXCEEDED");
        public static readonly ErrorCode RecordNotFound                   = ErrorCode.From("RECORD_NOT_FOUND");
        public static readonly ErrorCode LogPositionNotFound              = ErrorCode.From("LOG_POSITION_NOT_FOUND");

        // Declared last: static field initializers run in textual order, so the fields above must already be assigned.
        public static readonly FrozenSet<ErrorCode> All = FrozenSet.ToFrozenSet([
            StreamNotFound,
            StreamAlreadyExists,
            StreamDeleted,
            StreamTombstoned,
            StreamRevisionConflict,
            StreamRevisionNotFound,
            StreamAlreadyInAppendSession,
            AppendSessionNoRequests,
            AppendConsistencyViolation,
            MaxAppendRecordSizeExceeded,
            MaxAppendTransactionSizeExceeded,
            RecordNotFound,
            LogPositionNotFound
        ]);
    }

    /// <summary>
    /// Errors raised by the schema registry module. Pinned to <c>KurrentDB.Protocol.V2.Registry.Errors.RegistryError</c>.
    /// </summary>
    public static class Registry {
        public static readonly ErrorCode SchemaNotFound      = ErrorCode.From("SCHEMA_NOT_FOUND");
        public static readonly ErrorCode SchemaAlreadyExists = ErrorCode.From("SCHEMA_ALREADY_EXISTS");
        public static readonly ErrorCode SchemaDeleted       = ErrorCode.From("SCHEMA_DELETED");

        public static readonly FrozenSet<ErrorCode> All = FrozenSet.ToFrozenSet([
            SchemaNotFound,
            SchemaAlreadyExists,
            SchemaDeleted
        ]);
    }

    /// <summary>
    /// Errors raised by the secondary indexes module. Pinned to <c>KurrentDB.Protocol.V2.Indexes.Errors.IndexesError</c>.
    /// </summary>
    public static class Indexes {
        public static readonly ErrorCode IndexNotFound             = ErrorCode.From("INDEX_NOT_FOUND");
        public static readonly ErrorCode IndexAlreadyExists        = ErrorCode.From("INDEX_ALREADY_EXISTS");
        public static readonly ErrorCode IndexesNotReady           = ErrorCode.From("INDEXES_NOT_READY");
        public static readonly ErrorCode SecondaryIndexingDisabled = ErrorCode.From("SECONDARY_INDEXING_DISABLED");

        public static readonly FrozenSet<ErrorCode> All = FrozenSet.ToFrozenSet([
            IndexNotFound,
            IndexAlreadyExists,
            IndexesNotReady,
            SecondaryIndexingDisabled
        ]);
    }

    /// <summary>
    /// Errors raised by the server itself, independent of any module. Pinned to <c>Kurrent.Rpc.ServerError</c>.
    /// </summary>
    public static class Server {
        public static readonly ErrorCode NotLeaderNode           = ErrorCode.From("NOT_LEADER_NODE");
        public static readonly ErrorCode ServerNotReady          = ErrorCode.From("SERVER_NOT_READY");
        public static readonly ErrorCode ServerOverloaded        = ErrorCode.From("SERVER_OVERLOADED");
        public static readonly ErrorCode LeaderForwardingTimeout = ErrorCode.From("LEADER_FORWARDING_TIMEOUT");

        public static readonly FrozenSet<ErrorCode> All = FrozenSet.ToFrozenSet([
            NotLeaderNode,
            ServerNotReady,
            ServerOverloaded,
            LeaderForwardingTimeout
        ]);
    }

    /// <summary>
    /// Errors raised by the connectors module.
    /// </summary>
    /// <remarks>
    /// Not pinned: the connectors protocol lives under <c>proto/kurrentdb/protocol/v0</c>, which the client
    /// excludes from compilation, so there is no <c>ConnectorsError</c> enum to pin against. These values
    /// reproduce the codes the client matches on today.
    /// </remarks>
    public static class Connectors {
        public static readonly ErrorCode ConnectorNotFound      = ErrorCode.From("CONNECTOR_NOT_FOUND");
        public static readonly ErrorCode ConnectorAlreadyExists = ErrorCode.From("CONNECTOR_ALREADY_EXISTS");
        public static readonly ErrorCode ConnectorDeleted       = ErrorCode.From("CONNECTOR_DELETED");

        public static readonly FrozenSet<ErrorCode> All = FrozenSet.ToFrozenSet([
            ConnectorNotFound,
            ConnectorAlreadyExists,
            ConnectorDeleted
        ]);
    }
}
