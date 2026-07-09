using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Schema.Catalogs;

/// <summary>
/// A keyed collection of message type ↔ schema name mappings — the single user-facing
/// registration object. Catalogs are authored (by the source generator from assembly
/// attributes, or manually via <see cref="Map{T}()"/>) and consumed by clients, which
/// snapshot them at construction: the catalog stays a mutable template, live clients
/// are never affected by later changes.
/// </summary>
[PublicAPI]
public class MessageCatalog(string key = MessageCatalog.DefaultKey) {
    
    protected MessageCatalog(string key, Action<MessageCatalog> configure) : this(key) {
        Key = key;
        configure(this);
    }

    protected MessageCatalog(string key, bool alwaysActive, Action<MessageCatalog> configure) : this(key) {
        Key          = key;
        AlwaysActive = alwaysActive;
        configure(this);
    }

    public MessageCatalog(string key, bool alwaysActive, IReadOnlyList<MessageTypeDescriptor> descriptors) : this(key) {
        AlwaysActive = alwaysActive;

        foreach (var descriptor in descriptors)
            Add(descriptor);
    }

    public const string DefaultKey = "Default";

    static readonly ISchemaNameStrategy DefaultStrategy = new MessageSchemaNameStrategy();

    /// <summary>
    /// Identity used for selection (e.g. <c>MessageCatalogs.Get("orders")</c>).
    /// </summary>
    public string Key { get; } = key;

    /// <summary>
    /// When true, this catalog registers into every client's resolver by default. Other discovered
    /// catalogs are opt-in via <c>KurrentClientMessageOptions.UseCatalog</c>; an always-active
    /// catalog skips that step. Reserved for foundational SDK catalogs (system streams, streaming
    /// control messages); set via the always-active constructor or
    /// <c>[assembly: MessageCatalog(..., AlwaysActive = true)]</c>.
    /// </summary>
    public bool AlwaysActive { get; }

    ConcurrentBidirectionalDictionary<string, Type> TypeMap { get; } = new();

    public Type[] Types => TypeMap.Values.ToArray();

    public bool TryMap(SchemaName schemaName, Type messageType) =>
        TypeMap.TryAdd(schemaName, messageType);
    
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
    
    public MessageCatalog Add(MessageTypeDescriptor descriptor) =>
        Map(descriptor.ResolveSchemaName(), descriptor.MessageType);

    public bool TryAdd(MessageTypeDescriptor descriptor) =>
        TryMap(descriptor.ResolveSchemaName(), descriptor.MessageType);
    
    // Exposed for the client-construction union of multiple catalogs into one resolver
    // (MessageResolver.From, same assembly).
    internal IEnumerable<KeyValuePair<string, Type>> Entries => TypeMap;
}
