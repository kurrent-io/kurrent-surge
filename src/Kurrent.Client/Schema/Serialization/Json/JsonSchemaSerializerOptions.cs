using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Kurrent.Client.Schema.Serialization.Json;

[PublicAPI]
public record JsonSchemaSerializerOptions : SchemaSerializerOptions {
	public static JsonSchemaSerializerOptions Default => new();

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

	static JsonSerializerOptions CreateDefaultOptions() => BuildOptions([]);

	static JsonSerializerOptions BuildOptions(IReadOnlyList<JsonSerializerContext> userContexts) {
		var options = new JsonSerializerOptions {
			PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
			DictionaryKeyPolicy         = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
			UnknownTypeHandling         = JsonUnknownTypeHandling.JsonNode,
			UnmappedMemberHandling      = JsonUnmappedMemberHandling.Skip,
			NumberHandling              = JsonNumberHandling.AllowReadingFromString,
			Converters                  = {
				new NullableTimeSpanIso8601Converter(),
				new TimeSpanIso8601Converter()
			}
		};

		options.TypeInfoResolverChain.Add(KurrentJsonContext.Default);

		foreach (var context in userContexts)
			options.TypeInfoResolverChain.Add(context);

		// reflection fallback for types not covered by source-gen contexts
		options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

		options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
		return options;
	}
}
