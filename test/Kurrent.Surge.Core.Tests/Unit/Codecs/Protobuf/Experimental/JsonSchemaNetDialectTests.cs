using Google.Protobuf;
using Json.Schema;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using Transcoder.Probe;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// The dialect boundary of the second validator, measured against real exported documents rather
/// than fixtures. Node references its own type, which is the case that decides whether a recursive
/// message can be described in draft 2020-12 at all.
/// </summary>
/// <remarks>
/// Scoped to what the validator itself decides. Whether an exported document agrees with what the
/// writer emits is a separate question about the exporter and the writer, and it is measured in the
/// sibling protobuf tests.
/// </remarks>
public class JsonSchemaNetDialectTests {
    static readonly ProtobufJsonSchemaOptions Draft04     = new();
    static readonly ProtobufJsonSchemaOptions Draft202012 = new() { Dialect = JsonSchemaDialect.Draft202012 };

    const string OrderType = ProbeSchema.OrderTypeName;
    const string NodeType  = "transcoder.probe.Node";

    // Three levels deep, so the recursive $ref is followed rather than merely declared.
    static byte[] NodePayload() => new Node {
        Label = "root",
        Child = new Node { Label = "middle", Child = new Node { Label = "leaf" } }
    }.ToByteArray();

    [Test]
    public async ValueTask draft_2020_12_resolves_a_self_referencing_type() {
        // Arrange — a root $ref whose target references itself
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, NodeType, Draft202012);

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(ProbeSchema.Write(NodePayload(), NodeType));

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask draft_2020_12_rejects_an_undeclared_property_on_a_recursive_type() {
        // Arrange — accepting a payload proves nothing unless the validator also rejects one. The
        // exported objects are closed, so an undeclared member has to fail.
        var exported   = ProtobufJsonSchema.Export(ProbeSchema.Set, NodeType, Draft202012);
        var transcoded = ProbeSchema.Write(NodePayload(), NodeType).TrimEnd('}') + ",\"notAField\":1}";

        // Act
        var result = JsonSchemaNetAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async ValueTask draft_04_is_refused() {
        // Arrange — draft 4 is the dialect the other agent is kept for
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, OrderType, Draft04);

        // Act
        var parse = () => JsonSchemaNetAgent.Instance.ParseSchema(exported);

        // Assert
        await Assert.That(parse).Throws<JsonSchemaException>();
    }

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
