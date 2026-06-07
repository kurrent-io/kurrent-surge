using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.NameStrategies;

namespace Kurrent.Client;

/// <summary>
/// A keyed collection of message type ↔ schema name mappings — the single user-facing
/// registration object. Catalogs are authored (by the source generator from assembly
/// attributes, or manually via <see cref="Map{T}()"/>) and consumed by clients, which
/// snapshot them at construction: the catalog stays a mutable template, live clients
/// are never affected by later changes.
/// </summary>
[PublicAPI]
public class MessageCatalog(string key = MessageCatalog.DefaultKey) {
    public static MessageCatalog WithKey(string key) {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new(key);
    }

    public const string DefaultKey = "Default";

    static readonly ISchemaNameStrategy DefaultStrategy = new MessageSchemaNameStrategy();

    /// <summary>
    /// Identity used for selection (e.g. <c>MessageCatalogs.Get("orders")</c>).
    /// </summary>
    public string Key { get; } = key;

    ConcurrentBidirectionalDictionary<string, Type> TypeMap { get; } = new();

    public Type[] Types => TypeMap.Values.ToArray();

    /// <summary>
    /// Creates a catalog from registrations — the factory the source generator emits calls to.
    /// Registrations are self-contained: pin wins, then strategy, then the default convention.
    /// </summary>
    public static MessageCatalog From(string key, IReadOnlyList<MessageRegistration> registrations) {
        var catalog = new MessageCatalog(key);

        foreach (var registration in registrations)
            catalog.Map(registration.ResolveSchemaName(), registration.MessageType);

        return catalog;
    }

    public MessageCatalog Map(SchemaName schemaName, Type messageType) {
        TypeMap[schemaName] = messageType;
        return this;
    }

    public MessageCatalog Map<T>(string schemaName) =>
        Map(schemaName, typeof(T));

    public MessageCatalog Map<T>(ISchemaNameStrategy strategy) =>
        Map<T>(strategy.GenerateSchemaName<T>());

    public MessageCatalog Map<T>() =>
        Map<T>(DefaultStrategy);

    // Exposed for the client-construction union of multiple catalogs into one resolver.
    internal IEnumerable<KeyValuePair<string, Type>> Entries => TypeMap;

    internal MessageResolver GetResolver() {
        // Not "freezing" — the resolver gets a COPY, so any catalog change after this point
        // is invisible to it. The catalog stays mutable as a template; the copy happens at
        // client construction, which is when isolation matters.
        return new(new(TypeMap));
    }
}
