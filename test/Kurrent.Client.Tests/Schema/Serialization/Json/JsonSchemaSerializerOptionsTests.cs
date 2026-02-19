using System.Text.Json;
using System.Text.Json.Serialization;
using Kurrent.Client.Schema.Serialization.Json;
using Kurrent.Client.Tests.Schema.Serialization.Fixtures;

namespace Kurrent.Client.Tests.Schema.Serialization.Json;

public class JsonSchemaSerializerOptionsTests {
    [Test]
    public async Task default_options_has_camel_case_naming_policy() {
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        await Assert.That(options.PropertyNamingPolicy).IsEqualTo(JsonNamingPolicy.CamelCase);
    }

    [Test]
    public async Task default_options_ignores_null_when_writing() {
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        await Assert.That(options.DefaultIgnoreCondition).IsEqualTo(JsonIgnoreCondition.WhenWritingNull);
    }

    [Test]
    public async Task default_options_has_type_info_resolver_chain() {
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        await Assert.That(options.TypeInfoResolverChain).IsNotEmpty();
    }

    [Test]
    public async Task default_options_contains_kurrent_json_context_in_chain() {
        var options = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;

        await Assert.That(options.TypeInfoResolverChain).Contains(KurrentJsonContext.Default);
    }

    [Test]
    public async Task default_options_has_nodatime_converters() {
        var options    = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;
        var converters = options.Converters;

        var nodaTimeConverters = converters
            .Where(c => c.GetType().Assembly.GetName().Name == "NodaTime.Serialization.SystemTextJson")
            .ToList();

        await Assert.That(nodaTimeConverters).IsNotEmpty();
    }

    [Test]
    public async Task default_options_has_timespan_converters() {
        var options    = JsonSchemaSerializerOptions.DefaultJsonSerializerOptions;
        var converters = options.Converters;

        await Assert.That(converters.OfType<NullableTimeSpanIso8601Converter>().Count()).IsEqualTo(1);
        await Assert.That(converters.OfType<TimeSpanIso8601Converter>().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task json_serializer_options_returns_shared_instance_when_no_user_context() {
        var sut    = new JsonSchemaSerializerOptions();
        var result = sut.JsonSerializerOptions;

        await Assert.That(result).IsSameReferenceAs(JsonSchemaSerializerOptions.DefaultJsonSerializerOptions);
    }

    [Test]
    public async Task json_serializer_options_creates_new_instance_when_user_context_provided() {
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        var result = sut.JsonSerializerOptions;

        await Assert.That(result).IsNotSameReferenceAs(JsonSchemaSerializerOptions.DefaultJsonSerializerOptions);
    }

    [Test]
    public async Task user_context_is_added_to_resolver_chain() {
        var sut = new JsonSchemaSerializerOptions {
            UserJsonContexts = [TestJsonContext.Default]
        };

        var chain = sut.JsonSerializerOptions.TypeInfoResolverChain;

        await Assert.That(chain).Contains(TestJsonContext.Default);
    }

    [Test]
    public async Task use_protobuf_formatter_defaults_to_true() {
        var sut = new JsonSchemaSerializerOptions();

        await Assert.That(sut.UseProtobufFormatter).IsTrue();
    }
}
