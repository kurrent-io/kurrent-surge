using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kurrent.Surge.Client;
using Kurrent.Surge.Client.Streams;

namespace Kurrent.Surge.Codecs.Json;

[UsedImplicitly]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Dictionary<string, JsonValue>))]
[JsonSerializable(typeof(Metadata))]
[JsonConverter(typeof(StreamMetadataJsonConverter))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy   = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy    = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling         = JsonNumberHandling.AllowReadingFromString,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    UnknownTypeHandling    = JsonUnknownTypeHandling.JsonNode,
    UseStringEnumConverter = true,
    Converters             = [
        typeof(TimeSpanIso8601Converter),
        typeof(NullableTimeSpanIso8601Converter),
        // NodaTime
        typeof(InstantJsonConverter),
        typeof(IntervalJsonConverter),
        typeof(LocalDateJsonConverter),
        typeof(LocalTimeJsonConverter),
        typeof(LocalDateTimeJsonConverter),
        typeof(AnnualDateJsonConverter),
        typeof(YearMonthJsonConverter),
        typeof(DateIntervalJsonConverter),
        typeof(OffsetJsonConverter),
        typeof(DurationJsonConverter),
        typeof(PeriodJsonConverter),
        typeof(OffsetDateJsonConverter),
        typeof(OffsetTimeJsonConverter),
        typeof(OffsetDateTimeJsonConverter),
        typeof(TzdbDateTimeZoneJsonConverter),
        typeof(TzdbZonedDateTimeJsonConverter)
    ])]
partial class KurrentJsonContext : JsonSerializerContext;
