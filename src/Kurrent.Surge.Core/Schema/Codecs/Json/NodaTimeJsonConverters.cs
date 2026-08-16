using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace Kurrent.Surge.Codecs.Json;

/// <summary>
/// Forwarding wrapper around a NodaTime <see cref="JsonConverter"/> instance.
/// </summary>
/// <remarks>
/// NodaTime exposes its converters as pre-built instances — constructed with text patterns, and for two of
/// them an <see cref="IDateTimeZoneProvider"/> — so they cannot be named as parameterless types in
/// <c>[JsonSourceGenerationOptions(Converters = ...)]</c>, which requires types the source generator can
/// instantiate with <c>new()</c>. Each derived type supplies the inner converter through a parameterless
/// constructor and forwards to it, keeping the registration source-generated and reflection-free (AOT-safe).
/// </remarks>
abstract class NodaTimeJsonConverter<T>(JsonConverter inner) : JsonConverter<T> {
    // NodaConverters members are typed as the non-generic JsonConverter; the runtime instance is always the
    // JsonConverter<T> for the matching type, so the cast is total.
    readonly JsonConverter<T> _inner = (JsonConverter<T>)inner;

    public override bool CanConvert(Type typeToConvert) => _inner.CanConvert(typeToConvert);

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        _inner.Read(ref reader, typeToConvert, options);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        _inner.Write(writer, value, options);
}

sealed class InstantJsonConverter()        : NodaTimeJsonConverter<Instant>(NodaConverters.InstantConverter);
sealed class IntervalJsonConverter()       : NodaTimeJsonConverter<Interval>(NodaConverters.IntervalConverter);
sealed class LocalDateJsonConverter()      : NodaTimeJsonConverter<LocalDate>(NodaConverters.LocalDateConverter);
sealed class LocalTimeJsonConverter()      : NodaTimeJsonConverter<LocalTime>(NodaConverters.LocalTimeConverter);
sealed class LocalDateTimeJsonConverter()  : NodaTimeJsonConverter<LocalDateTime>(NodaConverters.LocalDateTimeConverter);
sealed class AnnualDateJsonConverter()     : NodaTimeJsonConverter<AnnualDate>(NodaConverters.AnnualDateConverter);
sealed class YearMonthJsonConverter()      : NodaTimeJsonConverter<YearMonth>(NodaConverters.YearMonthConverter);
sealed class DateIntervalJsonConverter()   : NodaTimeJsonConverter<DateInterval>(NodaConverters.DateIntervalConverter);
sealed class OffsetJsonConverter()         : NodaTimeJsonConverter<Offset>(NodaConverters.OffsetConverter);
sealed class DurationJsonConverter()       : NodaTimeJsonConverter<Duration>(NodaConverters.DurationConverter);
sealed class PeriodJsonConverter()         : NodaTimeJsonConverter<Period>(NodaConverters.RoundtripPeriodConverter);
sealed class OffsetDateJsonConverter()     : NodaTimeJsonConverter<OffsetDate>(NodaConverters.OffsetDateConverter);
sealed class OffsetTimeJsonConverter()     : NodaTimeJsonConverter<OffsetTime>(NodaConverters.OffsetTimeConverter);
sealed class OffsetDateTimeJsonConverter() : NodaTimeJsonConverter<OffsetDateTime>(NodaConverters.OffsetDateTimeConverter);

sealed class TzdbDateTimeZoneJsonConverter()
    : NodaTimeJsonConverter<DateTimeZone>(NodaConverters.CreateDateTimeZoneConverter(DateTimeZoneProviders.Tzdb));

sealed class TzdbZonedDateTimeJsonConverter()
    : NodaTimeJsonConverter<ZonedDateTime>(NodaConverters.CreateZonedDateTimeConverter(DateTimeZoneProviders.Tzdb));
