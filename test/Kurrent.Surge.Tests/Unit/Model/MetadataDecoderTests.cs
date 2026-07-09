using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bogus;
using KurrentDB.Diagnostics.Tracing;
using Kurrent.Surge.Client.Registry;
using Kurrent.Surge.Client.Streams;
using Kurrent.Schema;
using JsonSerializer = Kurrent.Surge.Client.Schema.Serialization.Json.JsonSerializer;

namespace Kurrent.Surge.Tests.Model;

/// <summary>
/// Round-trip coverage for the metadata JSON converter: what goes in must come back out
/// without losing data, in the shapes the decoder contract defines — booleans, Guids,
/// DateTimes, TimeSpans, and binary stay typed; numbers surface as invariant strings
/// (recoverable via <c>TryGetValue&lt;T&gt;</c>); everything else is a string.
/// </summary>
public class MetadataDecoderTests {
    [ClassDataSource<BogusFaker>(Shared = SharedType.PerTestSession)]
    public required BogusFaker Faker { get; [UsedImplicitly] init; }

    static readonly MetadataDecoderContext DecoderContext = new(StreamName.Unset, SchemaName.Unset, SchemaDataFormat.Json);

    static async ValueTask<Metadata> RoundTrip(Metadata metadata) {
        var bytes = new JsonSerializer().Serialize(metadata);
        return await new DefaultMetadataDecoder().Decode(bytes, DecoderContext);
    }

    [Test]
    public async ValueTask round_trips_typed_values() {
        // Arrange
        var guid = Faker.Random.Guid();
        var utc  = DateTime.UtcNow;
        var local = DateTime.Now;
        var span = Faker.Date.Timespan();

        var metadata = new Metadata()
            .With("bool-true", true)
            .With("bool-false", false)
            .With("guid", guid)
            .With("guid-empty", Guid.Empty)
            .With("datetime-utc", utc)
            .With("datetime-local", local)
            .With("timespan", span);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<bool>("bool-true")).IsTrue();
        await Assert.That(decoded.GetRequiredValue<bool>("bool-false")).IsFalse();
        await Assert.That(decoded.GetRequiredValue<Guid>("guid")).IsEqualTo(guid);
        await Assert.That(decoded.GetRequiredValue<Guid>("guid-empty")).IsEqualTo(Guid.Empty);
        await Assert.That(decoded.GetRequiredValue<DateTime>("datetime-utc")).IsEqualTo(utc);
        await Assert.That(decoded.GetRequiredValue<DateTime>("datetime-local")).IsEqualTo(local);
        await Assert.That(decoded.GetRequiredValue<TimeSpan>("timespan")).IsEqualTo(span);
    }

    [Test]
    public async ValueTask round_trips_binary_as_readonly_memory() {
        // Arrange
        // All binary shapes (byte[], Memory, ReadOnlyMemory) converge on base64 and decode
        // back as ReadOnlyMemory<byte> — the content must survive byte-for-byte.
        var bytes = Faker.Random.Bytes(32);

        var metadata = new Metadata()
            .With("from-array", bytes)
            .With("from-memory", new Memory<byte>(bytes))
            .With("from-readonly-memory", new ReadOnlyMemory<byte>(bytes));

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<ReadOnlyMemory<byte>>("from-array").ToArray()).IsEquivalentTo(bytes);
        await Assert.That(decoded.GetRequiredValue<ReadOnlyMemory<byte>>("from-memory").ToArray()).IsEquivalentTo(bytes);
        await Assert.That(decoded.GetRequiredValue<ReadOnlyMemory<byte>>("from-readonly-memory").ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask round_trips_numbers_without_losing_precision() {
        // Arrange
        // The decoder contract surfaces numbers as invariant strings; the data is lossless
        // because TryGetValue<T> converts back to the exact original value — extremes included.
        var metadata = new Metadata()
            .With("int", int.MaxValue)
            .With("long", long.MinValue)
            .With("float", float.MaxValue)
            .With("double", double.MaxValue)
            .With("decimal", decimal.MaxValue);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        // Raw storage shape is string per contract...
        await Assert.That(decoded["int"]).IsTypeOf<string>();

        // ...and the typed values are recovered exactly.
        await Assert.That(decoded.GetRequiredValue<int>("int")).IsEqualTo(int.MaxValue);
        await Assert.That(decoded.GetRequiredValue<long>("long")).IsEqualTo(long.MinValue);
        await Assert.That(decoded.GetRequiredValue<float>("float")).IsEqualTo(float.MaxValue);
        await Assert.That(decoded.GetRequiredValue<double>("double")).IsEqualTo(double.MaxValue);
        await Assert.That(decoded.GetRequiredValue<decimal>("decimal")).IsEqualTo(decimal.MaxValue);
    }

    [Test]
    public async ValueTask round_trips_strings_and_nulls() {
        // Arrange
        // Sentences are safe from the decoder's typed-parsing heuristics (not Guid, not
        // base64, not a date) so they must come back as the very same string.
        var text = Faker.Lorem.Sentence();

        var metadata = new Metadata()
            .With("text", text)
            .With("null-string", (string?)null)
            .With("null-guid", (Guid?)null)
            .With("null-int", (int?)null);

        // Decode always enriches the result with $schema.name and $schema.format from the
        // decoder context, so the round-tripped count is the original plus those two keys.
        var expectedCount = metadata.Count + 2;

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<string>("text")).IsEqualTo(text);
        await Assert.That(decoded["null-string"]).IsNull();
        await Assert.That(decoded["null-guid"]).IsNull();
        await Assert.That(decoded["null-int"]).IsNull();
        await Assert.That(decoded.Count).IsEqualTo(expectedCount);
    }

    [Test]
    public async ValueTask round_trips_system_schema_keys_typed() {
        // Arrange
        // ParseString re-types these from their well-known keys, so the typed system values
        // must survive the trip as their own types, not strings.
        var schemaName = SchemaName.From($"schema-{Faker.Random.AlphaNumeric(8)}");
        var format     = Faker.PickRandom(Enum.GetValues<SchemaDataFormat>().Where(f => f != SchemaDataFormat.Unspecified).ToArray());
        var versionId  = SchemaVersionId.From(Faker.Random.Guid());

        var metadata = new Metadata()
            .With(SystemMetadataKeys.SchemaName, schemaName)
            .With(SystemMetadataKeys.SchemaFormat, format)
            .With(SystemMetadataKeys.SchemaId, versionId);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<SchemaName>(SystemMetadataKeys.SchemaName)).IsEqualTo(schemaName);
        await Assert.That(decoded.GetRequiredValue<SchemaDataFormat>(SystemMetadataKeys.SchemaFormat)).IsEqualTo(format);
        await Assert.That(decoded.GetRequiredValue<SchemaVersionId>(SystemMetadataKeys.SchemaId)).IsEqualTo(versionId);
    }

    [Test]
    public async ValueTask round_trips_datetimeoffset_as_same_instant() {
        // Arrange
        // The wire format cannot distinguish DateTimeOffset from a zoned DateTime, and the
        // Read path tries DateTime first — the type morphs, but the instant must not.
        var offset          = Faker.Date.RecentOffset();
        var expectedInstant = offset.UtcDateTime;

        var metadata = new Metadata().With("dto", offset);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        var roundTripped = decoded.GetRequiredValue<DateTime>("dto");
        await Assert.That(roundTripped.ToUniversalTime()).IsEqualTo(expectedInstant);
    }

    [Test]
    public async ValueTask round_trips_nested_json_as_json_node() {
        // Arrange
        // Nested JSON values (JsonObject/JsonArray) must survive the trip structurally —
        // Write emits them via WriteTo, Read preserves them as JsonNode.
        var nestedObject = new JsonObject {
            ["name"]  = Faker.Person.FullName,
            ["count"] = Faker.Random.Int(min: 1),
            ["flags"] = new JsonArray(true, false)
        };

        var nestedArray = new JsonArray(1, 2, 3);

        var expectedObjectJson = nestedObject.ToJsonString();
        var expectedArrayJson  = nestedArray.ToJsonString();

        var metadata = new Metadata()
            .With("object", nestedObject)
            .With("array", nestedArray);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<JsonNode>("object").ToJsonString()).IsEqualTo(expectedObjectJson);
        await Assert.That(decoded.GetRequiredValue<JsonNode>("array").ToJsonString()).IsEqualTo(expectedArrayJson);
    }

    [Test]
    public async ValueTask decodes_legacy_nested_json_metadata() {
        // Arrange
        // Older clients write arbitrary JSON metadata, including nested objects — the decoder
        // must preserve them instead of rejecting the payload (backward compatibility).
        var bytes = """{"plain":"plain text","nested":{"x":1,"y":[true,null]}}"""u8.ToArray();

        // Act
        var decoded = await new DefaultMetadataDecoder().Decode(bytes, DecoderContext);

        // Assert
        await Assert.That(decoded.GetRequiredValue<string>("plain")).IsEqualTo("plain text");
        await Assert.That(decoded.GetRequiredValue<JsonNode>("nested").ToJsonString()).IsEqualTo("""{"x":1,"y":[true,null]}""");
    }

    [Test]
    [Arguments(double.NaN)]
    [Arguments(double.PositiveInfinity)]
    [Arguments(double.NegativeInfinity)]
    public async ValueTask write_throws_on_non_finite_floating_points(double value) {
        // Arrange
        // JSON cannot represent NaN/Infinity, and string fallbacks would collide with the
        // Read path's typed-parsing heuristics — non-finite values must fail loudly.
        var metadata = new Metadata().With("non-finite", value);

        // Act & Assert
        await Assert.That(() => new JsonSerializer().Serialize(metadata)).Throws<JsonException>();
    }

    [Test]
    public async ValueTask round_trips_user_enums_as_strings() {
        // Arrange
        // Enums write as their names; the stored value comes back as a string and the typed
        // retrieval path parses it back to the enum.
        var color        = Faker.PickRandom(Enum.GetValues<TestColor>());
        var expectedName = color.ToString();

        var metadata = new Metadata().With("color", color);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<string>("color")).IsEqualTo(expectedName);
        await Assert.That(decoded.GetRequiredValue<TestColor>("color")).IsEqualTo(color);
    }

    // Member name lengths deliberately avoid multiples of 4: a 4/8-char alphanumeric name
    // (e.g. "Blue") is valid base64 and the Read path's probe would morph it into bytes.
    enum TestColor { Crimson, Emerald, Indigo }

    [Test]
    public async ValueTask round_trips_trace_context_as_strings() {
        // Arrange
        // W3C trace IDs are 32/16-char hex strings — valid base64 alphabet at %4 lengths.
        // Without the key-driven exemption the decoder morphs them into bytes and
        // ExtractTraceContext silently returns null, killing distributed tracing.
        var traceId = ActivityTraceId.CreateRandom().ToString();
        var spanId  = ActivitySpanId.CreateRandom().ToString();

        var metadata = new Metadata()
            .With(TracingConstants.Metadata.TraceId, traceId)
            .With(TracingConstants.Metadata.SpanId, spanId);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded.GetRequiredValue<string>(TracingConstants.Metadata.TraceId)).IsEqualTo(traceId);
        await Assert.That(decoded.GetRequiredValue<string>(TracingConstants.Metadata.SpanId)).IsEqualTo(spanId);
    }

    [Test]
    [Arguments("abcd1234")] // valid base64 shape — the removed probe would have morphed it to bytes
    [Arguments("Infinity")] // 8 chars of base64 alphabet
    [Arguments("Blue")]     // 4 chars of base64 alphabet
    [Arguments("30")]       // TimeSpan.TryParse reads bare integers as days
    [Arguments("12:30")]    // contains ':' so it DOES type as TimeSpan — see typed retrieval below
    public async ValueTask user_strings_survive_decoding_without_morphing(string text) {
        // Arrange
        // Strings that merely look like other types must come back readable as the same
        // string — either stored verbatim or recovered via the TryGetValue<string> fallback.
        var metadata = new Metadata().With("text", text);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        var expected = text == "12:30" ? TimeSpan.Parse("12:30").ToString("c") : text;
        await Assert.That(decoded.GetRequiredValue<string>("text")).IsEqualTo(expected);
    }

    [Test]
    public async ValueTask stores_binary_as_base64_string_and_recovers_via_typed_retrieval() {
        // Arrange
        // The decoder no longer shape-sniffs strings into bytes; binary survives as its
        // base64 string in storage and the caller's declared type does the conversion.
        var bytes          = Faker.Random.Bytes(32);
        var expectedBase64 = Convert.ToBase64String(bytes);

        var metadata = new Metadata().With("blob", bytes);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded["blob"]).IsTypeOf<string>();
        await Assert.That(decoded.GetRequiredValue<string>("blob")).IsEqualTo(expectedBase64);
        await Assert.That(decoded.GetRequiredValue<byte[]>("blob")).IsEquivalentTo(bytes);
        await Assert.That(decoded.GetRequiredValue<ReadOnlyMemory<byte>>("blob").ToArray()).IsEquivalentTo(bytes);
    }

    [Test]
    public async ValueTask typed_values_are_retrievable_as_strings() {
        // Arrange
        // The decoder re-types Guid-shaped strings; the value must still be reachable as a
        // string through the stringify fallback (same formatting as Stringify()).
        var guid = Faker.Random.Guid();

        var metadata = new Metadata().With("id", guid);

        // Act
        var decoded = await RoundTrip(metadata);

        // Assert
        await Assert.That(decoded["id"]).IsTypeOf<Guid>();
        await Assert.That(decoded.GetRequiredValue<string>("id")).IsEqualTo(guid.ToString("D"));
    }

    [Test]
    public async ValueTask write_throws_on_unsupported_value_type() {
        // Arrange
        // The metadata value universe is closed: anything outside it must fail loudly at
        // write time instead of silently morphing into something that cannot round-trip.
        var metadata = new Metadata().With("unsupported", new UnsupportedValue());

        // Act & Assert
        await Assert.That(() => new JsonSerializer().Serialize(metadata)).Throws<JsonException>();
    }

    sealed class UnsupportedValue;
}
