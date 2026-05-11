using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Tests.Schema.Serialization.Fixtures;
using Kurrent.Testkit.Datasets.ArcadeRacer.Contracts;

namespace Kurrent.Client.Tests.Schema.Serialization.Json;

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
    public async Task default_options_has_nodatime_converters() {
        // Arrange
        var options    = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;
        var converters = options.Converters;

        // Act
        var nodaTimeConverters = converters
            .Where(c => c.GetType().Assembly.GetName().Name == "NodaTime.Serialization.SystemTextJson")
            .ToList();

        // Assert
        await Assert.That(nodaTimeConverters).IsNotEmpty();
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
}
