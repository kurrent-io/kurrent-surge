using Google.Protobuf;
using Json.Schema;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The dialect boundary of the validator, measured against real exported documents rather than
/// fixtures. Node references its own type, which is the case that decides whether a recursive
/// message can be described in draft 2020-12 at all.
/// </summary>
/// <remarks>
/// Scoped to what the validator itself decides. Whether an exported document agrees with what the
/// writer emits is a separate question, measured in the sibling protobuf tests.
/// </remarks>
public class JsonSchemaNetDialectTests {
    const string NodeType = "transcoder.probe.Node";

    static SchemaSource Node { get; } = SchemaSource.Text(ProbeSchema.Text, NodeType);

    // Three levels deep, so the recursive $ref is followed rather than merely declared.
    static string Transcoded() {
        var payload = new Transcoder.Probe.Node {
            Label = "root",
            Child = new Transcoder.Probe.Node { Label = "middle", Child = new Transcoder.Probe.Node { Label = "leaf" } }
        };

        return ProtobufSchemaTranscoder.Instance.ToJson(Node, payload.ToByteArray());
    }

    [Test]
    public async ValueTask resolves_a_self_referencing_type() {
        // Arrange — a root $ref whose target references itself
        var exported = ProtobufJsonSchema.Instance.ExportAsString(Node);

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(Transcoded());

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask rejects_an_undeclared_property_on_a_recursive_type() {
        // Arrange — accepting a payload proves nothing unless the validator also rejects one. The
        // exported objects are closed, so an undeclared member has to fail.
        var exported   = ProtobufJsonSchema.Instance.ExportAsString(Node);
        var transcoded = Transcoded().TrimEnd('}') + ",\"notAField\":1}";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask refuses_draft_04() {
        // Arrange — written out rather than exported, because the exporter emits one dialect now and
        // draft 4 is not it. This is the boundary the other agent is kept for.
        var draft04 = """
                      {"$schema":"http://json-schema.org/draft-04/schema#","type":"object",
                       "properties":{"label":{"type":"string"}}}
                      """;

        // Act
        var parse = () => JsonSchemaNetAgent.Instance.ParseSchema(draft04);

        // Assert
        await Assert.That(parse).Throws<JsonSchemaException>();
    }

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
