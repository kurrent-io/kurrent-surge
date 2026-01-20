using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Kurrent.Client.Schema.Serialization.Json;

[PublicAPI]
public record JsonSchemaSerializerOptions : SchemaSerializerOptions {
	public static readonly JsonSchemaSerializerOptions Default = new();

	public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = CreateDefaultOptions();

	static JsonSerializerOptions CreateDefaultOptions() {
		var options = new JsonSerializerOptions(JsonSerializerOptions.Default) {
			PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
			DictionaryKeyPolicy         = JsonNamingPolicy.CamelCase,
			PropertyNameCaseInsensitive = false,
			DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
			UnknownTypeHandling         = JsonUnknownTypeHandling.JsonNode,
			UnmappedMemberHandling      = JsonUnmappedMemberHandling.Skip,
			NumberHandling              = JsonNumberHandling.AllowReadingFromString,
			Converters                  = {
				new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
				new NullableTimeSpanIso8601Converter(),
				new TimeSpanIso8601Converter()
			}
		};

		options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
		return options;
	}

	public JsonSerializerOptions JsonSerializerOptions { get; init; } = DefaultJsonSerializerOptions;
	public bool                  UseProtobufFormatter  { get; init; } = true;
}
