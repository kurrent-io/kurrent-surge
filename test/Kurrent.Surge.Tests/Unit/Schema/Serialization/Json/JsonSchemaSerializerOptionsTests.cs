using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Kurrent.Surge.Client.Schema.Serialization.Json;
using Kurrent.Surge.Tests.Schema.Serialization.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;
using NodaTime;

namespace Kurrent.Surge.Tests.Schema.Serialization.Json;

[Category("SerDe")]
public class JsonSchemaSerializerOptionsTests {
    [Test]
    public async Task default_options_has_camel_case_naming_policy() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task default_options_ignores_null_when_writing() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.DefaultIgnoreCondition).IsEqualTo(JsonIgnoreCondition.WhenWritingNull);
    }

    [Test]
    public async Task default_options_use_camel_case_dictionary_keys() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.DictionaryKeyPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task default_options_allow_reading_numbers_from_string() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.NumberHandling).IsEqualTo(JsonNumberHandling.AllowReadingFromString);
    }

    [Test]
    public async Task default_options_skip_unmapped_members() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.UnmappedMemberHandling).IsEqualTo(JsonUnmappedMemberHandling.Skip);
    }

    [Test]
    public async Task default_options_handle_unknown_object_as_json_node() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.UnknownTypeHandling).IsEqualTo(JsonUnknownTypeHandling.JsonNode);
    }

    [Test]
    public async Task default_options_are_case_sensitive() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.PropertyNameCaseInsensitive).IsFalse();
    }

    [Test]
    public async Task default_options_has_type_info_resolver_chain() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.TypeInfoResolverChain).IsNotEmpty();
    }

    [Test]
    public async Task default_options_contains_kurrent_json_context_in_chain() {
        // Arrange
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Assert
        await Assert.That(options.TypeInfoResolverChain).Contains(KurrentJsonContext.Default);
    }

    [Test]
    public async Task default_options_inherit_context_converters() {
        // Arrange — the live options are a copy of the context's options, so their converters match exactly
        var contextConverters = KurrentJsonContext.Default.Options.Converters
            .Select(c => c.GetType())
            .ToHashSet();
        var liveConverters = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions.Converters
            .Select(c => c.GetType())
            .ToHashSet();

        // Assert
        await Assert.That(liveConverters.SetEquals(contextConverters)).IsTrue();
    }

    [Test]
    public async Task default_context_registers_every_nodatime_converter() {
        // Arrange — discover every NodaTime wrapper by type so the test cannot drift from the attribute list
        var wrappers = typeof(KurrentJsonContext).Assembly.GetTypes()
            .Where(t => t.BaseType is { IsGenericType: true } b
                     && b.GetGenericTypeDefinition() == typeof(NodaTimeJsonConverter<>))
            .ToHashSet();
        var registered = KurrentJsonContext.Default.Options.Converters
            .Select(c => c.GetType())
            .ToHashSet();

        // Act
        var missing = wrappers.Where(t => !registered.Contains(t)).ToList();

        // Assert
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task default_options_has_timespan_converters() {
        // Arrange
        var converters = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions.Converters;

        // Assert
        await Assert.That(converters.OfType<NullableTimeSpanIso8601Converter>().Count()).IsEqualTo(1);
        await Assert.That(converters.OfType<TimeSpanIso8601Converter>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task json_serializer_options_returns_shared_instance_when_no_user_context() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions();

        // Act
        var result = sut.JsonSerializerOptions;

        // Assert
        await Assert.That(result).IsSameReferenceAs(JsonSchemaSerializerOptions.DefaultJsonSerializerOptions);
    }

    [Test]
    public async Task json_serializer_options_creates_new_instance_when_user_context_provided() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        // Act
        var result = sut.JsonSerializerOptions;

        // Assert
        await Assert.That(result).IsNotSameReferenceAs(JsonSchemaSerializerOptions.DefaultJsonSerializerOptions);
    }

    [Test]
    public async Task user_context_is_added_to_resolver_chain() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        // Act
        var chain = sut.JsonSerializerOptions.TypeInfoResolverChain;

        // Assert
        await Assert.That(chain).Contains(TestJsonContext.Default);
    }

    [Test]
    public async Task use_protobuf_formatter_defaults_to_true() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions();

        // Assert
        await Assert.That(sut.UseProtobufFormatter).IsTrue();
    }

    [Test]
    public async Task user_context_resolves_type_info_for_registered_types() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        // Act
        var typeInfo = sut.JsonSerializerOptions.GetTypeInfo(typeof(LapCompleted));

        // Assert
        await Assert.That(typeInfo).IsNotNull();
        await Assert.That(typeInfo.OriginatingResolver).IsSameReferenceAs(TestJsonContext.Default);
    }

    [Test]
    public async Task user_context_serializes_and_deserializes_registered_types() {
        // Arrange
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        var expected = new LapCompleted {
            EventId        = Guid.NewGuid(),
            CarId          = "car-42",
            Lap            = 3,
            LapTime        = 78.5,
            CumulativeTime = 235.2,
            Timestamp      = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RaceId         = "race-1"
        };

        // Act
        var json   = System.Text.Json.JsonSerializer.Serialize(expected, sut.JsonSerializerOptions);
        var actual = System.Text.Json.JsonSerializer.Deserialize<LapCompleted>(json, sut.JsonSerializerOptions);

        // Assert
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.EventId).IsEqualTo(expected.EventId);
        await Assert.That(actual.CarId).IsEqualTo(expected.CarId);
        await Assert.That(actual.Lap).IsEqualTo(expected.Lap);
        await Assert.That(actual.LapTime).IsEqualTo(expected.LapTime);
        await Assert.That(actual.CumulativeTime).IsEqualTo(expected.CumulativeTime);
        await Assert.That(actual.RaceId).IsEqualTo(expected.RaceId);
    }

    [Test]
    public async Task source_gen_options_omit_reflection_resolver() {
        // Arrange — the reflection-free base is the shape produced under trimming / Native AOT
        var options = JsonSchemaSerializerOptions.CreateSourceGenOptions([]);

        // Act
        var hasReflectionResolver = options.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().Any();

        // Assert
        await Assert.That(hasReflectionResolver).IsFalse();
    }

    [Test]
    public async Task default_options_include_reflection_fallback_on_jit() {
        // Arrange — on a JIT host reflection is enabled, so BuildOptions appends the reflection fallback resolver
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        // Act
        var hasReflectionResolver = options.TypeInfoResolverChain.OfType<DefaultJsonTypeInfoResolver>().Any();

        // Assert
        await Assert.That(hasReflectionResolver).IsTrue();
    }

    [Test]
    public async Task source_gen_options_throw_for_unregistered_type() {
        // Arrange — no reflection fallback, so a type absent from every source-gen context fails loud
        var options = JsonSchemaSerializerOptions.CreateSourceGenOptions([]);
        var value   = new UnregisteredProbe("probe");

        // Assert
        await Assert.That(() => { _ = System.Text.Json.JsonSerializer.Serialize(value, options); })
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task default_options_serialize_unregistered_type_on_jit() {
        // Arrange — the JIT reflection fallback serializes a type absent from every source-gen context
        var options  = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;
        var value    = new UnregisteredProbe("probe");
        var expected = "probe";

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(value, options);

        // Assert
        await Assert.That(json).Contains(expected);
    }

    [Test]
    public async Task default_options_round_trip_nodatime_and_timespan_values() {
        // Arrange — JIT reflection resolves the unregistered holder; its NodaTime/TimeSpan members still
        // route through the wrapper converters carried by the live options.
        var options  = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;
        var utc      = DateTimeZoneProviders.Tzdb["UTC"];
        var expected = new TemporalProbe(
            Instant.FromUtc(2026, 6, 17, 10, 30),
            new LocalDate(2026, 6, 17),
            Instant.FromUtc(2026, 6, 17, 10, 30).InZone(utc),
            TimeSpan.FromMinutes(90));

        // Act
        var json   = System.Text.Json.JsonSerializer.Serialize(expected, options);
        var actual = System.Text.Json.JsonSerializer.Deserialize<TemporalProbe>(json, options);

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    record UnregisteredProbe(string Name);

    record TemporalProbe(Instant Moment, LocalDate Date, ZonedDateTime Zoned, TimeSpan Elapsed);
}
