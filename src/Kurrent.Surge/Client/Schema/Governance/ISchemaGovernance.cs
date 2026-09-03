using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Schema.Governance;

/// <summary>
/// Schema registry governance as a standalone service, with catalog resolution evicted to
/// pipeline steps. Both methods receive already-resolved inputs and short-circuit
/// synchronously when the client's schema options require nothing.
/// </summary>
interface ISchemaGovernance {
    /// <summary>
    /// The catalog walk's per-entry step: registers or validates one schema. Returns
    /// <see cref="SchemaVersionId.Unset"/> when the client's options require nothing.
    /// </summary>
    ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, CancellationToken ct);

    /// <summary>
    /// Read path, before decode. Ensures the wire schema is compatible with the
    /// already-resolved type. Returns the version id when the record arrived without one.
    /// </summary>
    ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, CancellationToken ct);

    /// <summary>
    /// The single-flight catalog registration: registers the client's catalogs with the
    /// server — or verifies them when registration is manual and validation is on — and
    /// carries the complete wire schema info for every entry. The first awaiter starts it;
    /// every awaiter shares the same task, faults included. Schema options that require no
    /// registry contact yield an already-completed empty result.
    /// </summary>
    Task<CatalogSchemaMap> CatalogRegistration { get; }
}
