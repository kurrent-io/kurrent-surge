using System.Text.Json;
using KurrentJsonContext = Kurrent.Surge.Codecs.Json.KurrentJsonContext;

namespace Kurrent.Surge.Client;

// Both calls pass the source-generated context: the metastream carries record metadata, and trimming
// or AOT removes the reflection resolver, which fails the metastream write and the checkpoint load.
static class MetadataJsonExtensions {
    extension(Metadata metadata) {
        public JsonDocument ToJsonDocument() =>
            JsonDocument.Parse(JsonSerializer.Serialize(metadata, KurrentJsonContext.Default.Metadata));
    }

    extension(Metadata) {
        public static Metadata FromJsonDocument(JsonDocument? document) =>
            document is null ? new() : document.Deserialize(KurrentJsonContext.Default.Metadata) ?? new();
    }
}
