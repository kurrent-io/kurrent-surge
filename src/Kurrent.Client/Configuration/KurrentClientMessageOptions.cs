using Kurrent.Client.Registry;
using Kurrent.Client.Schema.NameStrategies;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Schema.Serialization.Protobuf;

namespace Kurrent.Client;

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
        var union = new ConcurrentBidirectionalDictionary<string, Type>();

        var discovered = MessageCatalogDiscovery.GetCatalogs();

        // AlwaysActive catalogs are foundational and register unconditionally; everything else
        // discovered is opt-in and arrives only through UseCatalog (UsedCatalogs).
        List<MessageCatalog> catalogs = [Catalog, ..UsedCatalogs, ..discovered.Where(c => c.AlwaysActive)];

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

        return new(union);
    }
}
