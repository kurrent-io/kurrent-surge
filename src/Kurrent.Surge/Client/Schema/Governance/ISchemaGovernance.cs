using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Client.Schema.Governance;

/// <summary>
/// Schema registry governance as a standalone service: the effectful half of today's
/// <c>SchemaManager</c>, with catalog resolution evicted to pipeline steps. Both methods
/// receive already-resolved inputs and short-circuit synchronously when the policy
/// requires nothing.
/// </summary>
interface ISchemaGovernance {
    /// <summary>
    /// Write path, before encode. Registers or validates the schema per policy.
    /// Returns <see cref="SchemaVersionId.Unset"/> when the policy requires nothing.
    /// </summary>
    ValueTask<SchemaVersionId> EnsureRegistered(SchemaName schema, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct);

    /// <summary>
    /// Read path, before decode. Ensures the wire schema is compatible with the
    /// already-resolved type. Returns the version id when the record arrived without one.
    /// </summary>
    ValueTask<SchemaVersionId> EnsureCompatible(RecordSchemaInfo schemaInfo, Type messageType, ResolvedSchemaRegistryPolicy policy, CancellationToken ct);

    /// <summary>
    /// The single-flight catalog registration: registers the client's catalogs with the
    /// server — or verifies them when the policy forbids registration — and carries the
    /// complete wire schema info for every entry. The first awaiter starts it; every
    /// awaiter shares the same task, faults included. A policy that requires no registry
    /// contact yields an already-completed empty result.
    /// </summary>
    Task<CatalogSchemaMap> CatalogRegistration { get; }

    /// <summary>
    /// Starts the catalog registration without awaiting it.
    /// </summary>
    void StartCatalogRegistration();
}
