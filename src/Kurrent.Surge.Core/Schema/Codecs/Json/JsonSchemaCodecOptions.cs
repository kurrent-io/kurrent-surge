using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Kurrent.Surge.Codecs.Json;

[PublicAPI]
public sealed class JsonSchemaCodecOptions : SchemaCodecOptions {
    public static JsonSchemaCodecOptions Default => new();

    public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = BuildOptions([]);

    /// <summary>
    /// User-provided <see cref="JsonSerializerContext"/> instances for domain type serialization.
    /// When provided, they are added to the <see cref="System.Text.Json.JsonSerializerOptions.TypeInfoResolverChain"/>
    /// alongside the internal <see cref="KurrentJsonContext"/>.
    /// </summary>
    public IReadOnlyList<JsonSerializerContext> UserJsonContexts { get; set; } = [];

    public JsonSerializerOptions JsonSerializerOptions =>
        UserJsonContexts.Count == 0
            ? DefaultJsonSerializerOptions
            : BuildOptions(UserJsonContexts);

    public bool UseProtobufFormatter { get; set; } = true;
    
    static JsonSerializerOptions BuildOptions(IReadOnlyList<JsonSerializerContext> userContexts) {
        var options = CreateSourceGenOptions(userContexts);

        // Reflection fallback for types not covered by any source-generated context.
        // JsonSerializer.IsReflectionEnabledByDefault is a link-time feature switch: used inline it lets the
        // analyzer prove this branch safe (no IL2026 / IL3050) and trimming / Native AOT folds it away so the
        // reflection resolver is never rooted, while a default JIT consumer keeps reflection as a convenience.
        // https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation#disable-reflection-defaults
        if (System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault)
            options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        return options;
    }

    // Reflection-free base shared by the public path and tests: the source-generated context's own options —
    // camelCase / ignore-null / number- & unknown-type handling, the TimeSpan + NodaTime converters, and
    // KurrentJsonContext as the resolver, all from [JsonSourceGenerationOptions] — plus any user contexts.
    internal static JsonSerializerOptions CreateSourceGenOptions(IReadOnlyList<JsonSerializerContext> userContexts) {
        var options = new JsonSerializerOptions(KurrentJsonContext.Default.Options);

        foreach (var context in userContexts)
            options.TypeInfoResolverChain.Add(context);

        return options;
    }
}
