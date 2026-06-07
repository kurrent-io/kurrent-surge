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
    /// When true (default), every catalog exported by loaded assemblies (via their generated
    /// catalog providers) is included automatically — declare with attributes, configure
    /// nothing. Disable for strict per-client selection via <see cref="UseCatalog"/>.
    /// </summary>
    public bool AutoUseCatalogs { get; set; } = true;

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
    /// Adds catalogs to this client explicitly. Composes with <see cref="AutoUseCatalogs"/>;
    /// duplicates (same instance reached both ways) are unioned harmlessly.
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
    /// The freeze: unions the system catalog, the default catalog, explicit catalogs, and
    /// (when enabled) auto-discovered catalogs into one snapshot resolver. Conflicting
    /// mappings fail construction loudly, naming the offending catalog.
    /// </summary>
    internal MessageResolver BuildResolver() {
        var union = new ConcurrentBidirectionalDictionary<string, Type>();

        List<MessageCatalog> catalogs = [SystemMessageCatalog.Instance, Catalog, ..UsedCatalogs];

        if (AutoUseCatalogs)
            catalogs.AddRange(MessageCatalogDiscovery.GetCatalogs());

        // The same catalog instance may arrive both explicitly and via auto-discovery.
        var seen = new HashSet<MessageCatalog>(ReferenceEqualityComparer.Instance);

        foreach (var catalog in catalogs) {
            if (!seen.Add(catalog))
                continue;

            foreach (var entry in catalog.Entries) {
                try {
                    union[entry.Key] = entry.Value;
                }
                catch (ArgumentException ex) {
                    throw new InvalidOperationException(
                        $"Catalog '{catalog.Key}' has a conflicting mapping for schema '{entry.Key}' / type {entry.Value.FullName}: {ex.Message}", ex);
                }
            }
        }

        return new(union);
    }
}
