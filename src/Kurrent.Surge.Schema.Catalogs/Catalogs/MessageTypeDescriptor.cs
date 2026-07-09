using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Schema.Catalogs;

/// <summary>
/// A self-contained message mapping: it carries everything needed to resolve its schema
/// name — a pinned name, a strategy, or the default <see cref="MessageSchemaNameStrategy"/>.
/// The currency of the catalog system: generated catalogs, manual maps, and assembly-level
/// declarations all produce these.
/// </summary>
/// <param name="MessageType">The message type to map.</param>
/// <param name="SchemaName">Pinned wire-level schema name; wins over any strategy.</param>
/// <param name="Strategy">Naming strategy; null falls back to <see cref="MessageSchemaNameStrategy"/>.</param>
/// <param name="Catalog">Key of the catalog this descriptor belongs to; null → the "Default" catalog.</param>
[PublicAPI]
public sealed record MessageTypeDescriptor(
    Type MessageType,
    string? SchemaName = null,
    ISchemaNameStrategy? Strategy = null,
    string? Catalog = null
) {
    // The fallback when a descriptor carries neither a pin nor a strategy
    // (e.g. bare [assembly: MessageCatalog("Prefix")] scans).
    static readonly ISchemaNameStrategy DefaultStrategy = new MessageSchemaNameStrategy();

    /// <summary>
    /// Resolves the wire-level schema name: pinned name first, then the descriptor's
    /// strategy, then <see cref="MessageSchemaNameStrategy"/>.
    /// </summary>
    public SchemaName ResolveSchemaName() {
        if (SchemaName is not null)
            return SchemaName;

        return (Strategy ?? DefaultStrategy).GenerateSchemaName(MessageType);
    }
}
