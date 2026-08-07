using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Client.Schema.Serialization.Protobuf;
using Kurrent.Surge.Schema.Catalogs;
using Kurrent.Surge.Schema.Naming;

namespace Kurrent.Surge.Client;

/// <summary>
/// Client message handling: which message types this client speaks and what wire names they
/// carry. Server-side schema governance (registration/validation) lives in the schema options;
/// this is the client's vocabulary.
/// </summary>
[PublicAPI]
public class KurrentClientMessageOptions {
    /// <summary>
    /// The "Default" catalog — config-time <see cref="MapMessage{T}()"/> calls land here.
    /// </summary>
    public MessageCatalog Catalog { get; set; } = new();

    /// <summary>
    /// Configuration options for the JSON schema serializer.
    /// </summary>
    public JsonSchemaSerializerOptions Json { get; set; } = JsonSchemaSerializerOptions.Default;

    /// <summary>
    /// Configuration options for the Protobuf schema serializer.
    /// </summary>
    public ProtobufSchemaSerializerOptions Protobuf { get; set; } = ProtobufSchemaSerializerOptions.Default;

    List<MessageCatalog> UsedCatalogs { get; } = [];

    /// <summary>
    /// Adds discovered (or hand-built) catalogs to this client. Catalogs that aren't
    /// <see cref="MessageCatalog.AlwaysActive"/> are opt-in this way; duplicates (the same
    /// instance reached more than once) are unioned harmlessly.
    /// </summary>
    public KurrentClientMessageOptions UseCatalog(params IEnumerable<MessageCatalog> catalogs) {
        UsedCatalogs.AddRange(catalogs);
        return this;
    }

    internal List<Func<ISchemaManager, ISchemaSerializer>> SerializerFactories { get; } = [];

    /// <summary>
    /// Registers a schema serializer, deferred until the client builds its own <see cref="ISchemaManager"/>.
    /// The built-in JSON, Protobuf, and bytes-passthrough serializers register through this exact same
    /// path (see <c>KurrentClientMessageOptionsExtensions.UseJson</c>/<c>UseProtobuf</c>/<c>UseBytes</c> in
    /// Kurrent.Surge) — there is no separate mechanism for built-ins versus custom serializers.
    /// </summary>
    public KurrentClientMessageOptions UseSerializer(params IEnumerable<Func<ISchemaManager, ISchemaSerializer>> factories) {
        SerializerFactories.AddRange(factories);
        return this;
    }

    public KurrentClientMessageOptions MapMessage<T>(string schemaName) {
        Catalog.Map<T>(schemaName);
        return this;
    }

    public KurrentClientMessageOptions MapMessage<T>(ISchemaNameStrategy strategy) {
        Catalog.Map<T>(strategy);
        return this;
    }

    public KurrentClientMessageOptions MapMessage<T>() {
        Catalog.Map<T>();
        return this;
    }

    /// <summary>
    /// The freeze: unions the default catalog, explicitly used catalogs, and every always-active
    /// catalog into one snapshot resolver. Discovery always runs, but only
    /// <see cref="MessageCatalog.AlwaysActive"/> catalogs (system streams, streaming contracts)
    /// register by default — the rest are opt-in via <see cref="UseCatalog"/>. Conflicting
    /// mappings fail construction loudly, naming the offending catalog.
    /// </summary>
    internal MessageResolver BuildResolver() {
        var discovered = MessageCatalogDiscovery.GetCatalogs();

        // AlwaysActive catalogs are foundational and register unconditionally; everything else
        // discovered is opt-in and arrives only through UseCatalog (UsedCatalogs). The union, dedup,
        // and conflict detection (the freeze) now live in MessageResolver.From.
        return MessageResolver.From([Catalog, ..UsedCatalogs, ..discovered.Where(c => c.AlwaysActive)]);
    }
}
