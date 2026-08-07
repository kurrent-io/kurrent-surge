using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

namespace Kurrent.Surge.Experimental;

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
    /// Validates with the server that the catalogs' messages can be produced — registering or
    /// checking compatibility per policy. Idempotent through the caches; a policy that requires
    /// nothing makes it a no-op.
    /// </summary>
    Task ValidateCatalog(IEnumerable<MessageCatalog> catalogs, ResolvedSchemaRegistryPolicy policy, CancellationToken ct);
}
