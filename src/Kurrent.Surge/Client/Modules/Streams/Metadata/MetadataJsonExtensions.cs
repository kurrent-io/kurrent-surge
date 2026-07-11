using System.Text.Json;

namespace Kurrent.Surge.Client;

static class MetadataJsonExtensions {
    extension(Metadata metadata) {
        public JsonDocument ToJsonDocument() => JsonDocument.Parse(JsonSerializer.Serialize(metadata));
    }

    extension(Metadata) {
        public static Metadata FromJsonDocument(JsonDocument? document) =>
            document is null ? new() : document.Deserialize<Metadata>() ?? new();
    }
}
