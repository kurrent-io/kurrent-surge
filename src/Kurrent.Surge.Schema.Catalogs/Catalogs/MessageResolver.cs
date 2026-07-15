namespace Kurrent.Surge.Schema.Catalogs;

/// <summary>
/// Read-only message type ↔ schema name resolution over a snapshot taken at client
/// construction. With auto-mapping removed nothing ever writes after the copy, so every
/// lookup is a lock-free dictionary hit: mapped → resolved; unmapped → throw (append) or
/// null (read). The resolver is pure lookup — whether an unresolved READ is an error is a
/// per-operation decision (read/subscribe options), enforced higher in the call stack.
/// </summary>
public sealed class MessageResolver {
    readonly ConcurrentBidirectionalDictionary<string, Type> _typeMap;

    // Internal: resolvers are only created by catalog snapshots at client construction.
    internal MessageResolver(ConcurrentBidirectionalDictionary<string, Type> typeMap) =>
        _typeMap = typeMap;

    /// <summary>
    /// The freeze: unions a set of catalogs into one immutable snapshot resolver. The same catalog
    /// instance reached more than once is deduplicated by reference; identical re-registration of a
    /// mapping is tolerated, but a genuine conflict — the same schema name mapped to a different type,
    /// or the same type to a different name — fails construction loudly, naming the offending catalog.
    /// </summary>
    public static MessageResolver From(params IEnumerable<MessageCatalog> catalogs) {
        var union = new ConcurrentBidirectionalDictionary<string, Type>();

        // The same catalog instance may arrive both explicitly and via discovery.
        var seen = new HashSet<MessageCatalog>(ReferenceEqualityComparer.Instance);

        foreach (var catalog in catalogs) {
            if (!seen.Add(catalog))
                continue;

            foreach (var entry in catalog.Entries) {
                // Tolerate identical re-registration; fail loud on a genuine conflict — the same
                // schema name resolving to a different type would silently corrupt decoding.
                if (union.TryGetValue(entry.Key, out var existing)) {
                    if (existing == entry.Value)
                        continue;

                    throw new InvalidOperationException(
                        $"Catalog '{catalog.Key}' maps schema '{entry.Key}' to {entry.Value.FullName}, " +
                        $"but it is already mapped to {existing.FullName}.");
                }

                try {
                    union[entry.Key] = entry.Value;
                }
                catch (ArgumentException ex) {
                    // Reverse conflict: the same type is already mapped to a different schema name.
                    throw new InvalidOperationException(
                        $"Catalog '{catalog.Key}' has a conflicting mapping for schema '{entry.Key}' / type {entry.Value.FullName}: {ex.Message}", ex);
                }
            }
        }

        return new MessageResolver(union);
    }

    /// <summary>
    /// Append path. An unmapped type on append is always an error — there is no
    /// auto-mapping: if it's mapped it's there, if it's not it throws.
    /// </summary>
    public SchemaName ResolveSchemaName(Type messageType) =>
        _typeMap.TryGetKey(messageType, out var schemaName)
            ? schemaName
            : throw new SchemaNameNotMappedException(messageType);

    /// <summary>
    /// Read path. An unresolved schema name returns null so the record can pass through
    /// raw (processors have fallback handlers); strict consumers opt into throwing via
    /// per-operation read/subscribe options.
    /// </summary>
    public Type? ResolveType(SchemaName schemaName) =>
        _typeMap.TryGetValue(schemaName, out var messageType) ? messageType : null;
}

/// <summary>
/// Thrown when a schema name cannot be resolved to a message type and the client (or the
/// governance policy) requires resolution.
/// </summary>
[PublicAPI]
public sealed class MessageTypeResolutionException(SchemaName schemaName)
    : Exception($"Schema '{schemaName}' is not mapped to a message type. Map it in a message catalog or with options.Messages.") {
    public SchemaName SchemaName { get; } = schemaName;
}

/// <summary>
/// Thrown when appending a message whose type has no schema name mapping in any catalog
/// used by the client.
/// </summary>
[PublicAPI]
public sealed class SchemaNameNotMappedException(Type messageType)
    : Exception($"Message type {messageType.FullName} is not mapped to a schema name. Map it in a message catalog or with options.Messages.") {
    public Type MessageType { get; } = messageType;
}
