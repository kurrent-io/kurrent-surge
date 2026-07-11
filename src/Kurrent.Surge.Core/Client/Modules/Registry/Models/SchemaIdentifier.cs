using Kurrent.Surge.Schema;
using Kurrent.Variant;

namespace Kurrent.Surge.Client.Registry;

public readonly partial record struct SchemaIdentifier : IVariant<SchemaName, SchemaVersionId> {
    /// <summary>
    /// True when this identifier was constructed via <c>default</c> (no inner value) or when the
    /// inner <see cref="SchemaName"/>/<see cref="SchemaVersionId"/> is itself unset.
    /// </summary>
    public bool IsUnset => Value is SchemaName { IsUnset: true } or SchemaVersionId { IsUnset: true };
}
