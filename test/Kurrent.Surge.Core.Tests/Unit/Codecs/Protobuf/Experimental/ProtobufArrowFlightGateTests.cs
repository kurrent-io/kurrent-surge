using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using Kurrent.Testkit.TUnit;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The two capabilities working together, on a protocol nobody here wrote.
/// </summary>
/// <remarks>
/// A payload is built from types generated at build time, transcoded by a writer that has only the
/// schema text, and then validated against the schema exported from that same text. Three
/// independent readings of one <c>.proto</c> file have to agree.
/// </remarks>
public class ProtobufArrowFlightGateTests {
    static string ProtoRoot { get; } = Path.Combine(AppContext.BaseDirectory, "protos");

    /// <summary>The schema as text, which is all the writer and the exporter ever see.</summary>
    static IReadOnlyList<string> SchemaTexts { get; } =
        Directory.EnumerateFiles(Path.Combine(ProtoRoot, "arrow"), "*.proto", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

    /// <summary>The same schema as types, which is the oracle.</summary>
    static TypeRegistry Types { get; } = TypeRegistry.FromFiles(
        Apache.Arrow.Flight.Protocol.FlightReflection.Descriptor,
        Arrow.Flight.Protocol.Sql.FlightSqlReflection.Descriptor);

    static JsonFormatter Oracle { get; } = new(JsonFormatter.Settings.Default.WithTypeRegistry(Types));

    /// <summary>The schema text every message is exported and transcoded from.</summary>
    /// <remarks>Cached per message so both gates share one resolved schema rather than parsing twice.</remarks>
    static readonly Dictionary<string, SchemaSource> Sources = new(StringComparer.Ordinal);

    static SchemaSource Source(string messageTypeName) {
        lock (Sources) {
            if (Sources.TryGetValue(messageTypeName, out var cached)) return cached;

            return Sources[messageTypeName] = SchemaSource.From(messageTypeName, SchemaTexts);
        }
    }

    /// <summary>Every message the two generated files declare, nested ones included.</summary>
    static IEnumerable<string> DeclaredMessages(IEnumerable<MessageDescriptor> messages) {
        foreach (var message in messages) {
            yield return message.FullName;

            foreach (var nested in DeclaredMessages(message.NestedTypes)) yield return nested;
        }
    }

    public sealed class ArrowMessages : TestCaseDataGenerator<string> {
        protected override IEnumerable<TestCase<string>> Data() =>
            DeclaredMessages(Apache.Arrow.Flight.Protocol.FlightReflection.Descriptor.MessageTypes)
                .Concat(DeclaredMessages(Arrow.Flight.Protocol.Sql.FlightSqlReflection.Descriptor.MessageTypes))
                // A map entry is synthesized by the language rather than declared, so nothing exports one.
                .Where(name => !name.EndsWith("Entry", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new TestCase<string>(name, DisplayName: name));
    }

    [Test]
    [ArrowMessages]
    public async ValueTask a_transcoded_payload_validates_against_the_exported_schema(string messageTypeName) {
        // Arrange
        var source   = Source(messageTypeName);
        var payload  = Populated(messageTypeName).ToByteArray();
        var exported = ProtobufJsonSchema.Instance.ExportAsString(source);

        // Act
        var transcoded = ProtobufSchemaTranscoder.Instance.ToJson(source, payload);
        var result     = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid)
            .IsTrue()
            .Because($"{transcoded} — {string.Join("; ", result.Errors.Select(error => error.ToString()))}");
    }

    [Test]
    [ArrowMessages]
    public async ValueTask transcodes_a_payload_the_way_google_does(string messageTypeName) {
        // Arrange — the writer resolves the schema at run time; the oracle was handed types at build
        // time. Agreement is what says the schema-driven path reads the wire correctly.
        var message  = Populated(messageTypeName);
        var expected = Normalize(Oracle.Format(message));

        // Act
        var actual = Normalize(ProtobufSchemaTranscoder.Instance.ToJson(Source(messageTypeName), message.ToByteArray()));

        // Assert
        await Assert.That(actual).IsEqualTo(expected);
    }

    #region ->> Payload construction <<-

    /// <remarks>
    /// Every scalar, enum and string field carries a value, so a message contributes something to the
    /// comparison rather than serialising to nothing. Repeated, map and message fields are left absent:
    /// filling them needs recursion the corpus does not need to be exercised.
    /// </remarks>
    static IMessage Populated(string messageTypeName) {
        var descriptor = Types.Find(messageTypeName)
                      ?? throw new InvalidOperationException($"'{messageTypeName}' has no generated type.");

        // The parser builds a default instance without reflection, which is the AOT-safe way in.
        var message = descriptor.Parser.ParseFrom(ReadOnlySpan<byte>.Empty);

        foreach (var field in descriptor.Fields.InDeclarationOrder()) {
            if (field.IsRepeated || field.IsMap) continue;

            if (ValueFor(field) is { } value) field.Accessor.SetValue(message, value);
        }

        return message;
    }

    static object? ValueFor(FieldDescriptor field) => field.FieldType switch {
        Type.Bool => true,

        Type.Int32 or Type.SInt32 or Type.SFixed32 => 7,
        Type.UInt32 or Type.Fixed32                => 7u,
        Type.Int64 or Type.SInt64 or Type.SFixed64 => 7L,
        Type.UInt64 or Type.Fixed64                => 7UL,

        Type.Float  => 1.5f,
        Type.Double => 2.5d,

        Type.String => "arrow",
        Type.Bytes  => ByteString.CopyFrom(1, 2, 3),

        // The first declared value, which every enum has and which is not always zero.
        Type.Enum => field.EnumType.Values[0].Number,

        // A message or group would need an instance built for it, and an absent one is still a
        // legitimate payload the schema has to accept.
        _ => null
    };

    #endregion // Payload construction

    // Google writes UTF-16 with its own spacing; the writer writes compact UTF-8. Comparing parsed
    // documents rather than text keeps the difference from being mistaken for a disagreement.
    static string Normalize(string json) => JsonNode.Parse(json)!.ToJsonString();
}
