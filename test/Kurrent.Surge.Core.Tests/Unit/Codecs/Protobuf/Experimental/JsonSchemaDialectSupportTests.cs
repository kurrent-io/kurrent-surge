using Google.Protobuf;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Validation;
using Transcoder.Probe;

namespace Kurrent.Surge.Core.Tests.Unit.Codecs.Protobuf.Experimental;

/// <summary>
/// Which JSON Schema draft the registry's validator can actually read.
/// </summary>
/// <remarks>
/// Draft 4 is the current default because NJsonSchema was reported unable to resolve a <c>$ref</c> into
/// <c>$defs</c> — it threw rather than failing, and a root <c>$ref</c> whose target references itself was
/// named as the case that broke. That report predates NJsonSchema 11.6.1. These tests decide it by
/// measurement, because the answer determines whether the platform can hold one dialect or two.
/// </remarks>
public class JsonSchemaDialectSupportTests {
    static readonly ProtobufJsonSchemaOptions Draft04     = new();
    static readonly ProtobufJsonSchemaOptions Draft202012 = new() { Dialect = JsonSchemaDialect.Draft202012 };

    /// <summary>Reaches two nested messages, an enum, a map and seven well-known types.</summary>
    const string OrderType = ProbeSchema.OrderTypeName;

    /// <summary>Recursive: its only message field is its own type. The case the report named.</summary>
    const string NodeType = "transcoder.probe.Node";

    static byte[] OrderPayload() => new Order {
        OrderId  = "8d2b1f6c",
        Priority = Priority.High,
        Ratio    = 0.125,
        Sequence = ulong.MaxValue,
        Note     = "handle with care",
        Tags     = { ["channel"] = "web" }
    }.ToByteArray();

    // Three levels deep, so the recursive $ref is followed rather than merely declared.
    static byte[] NodePayload() => new Node {
        Label = "root",
        Child = new Node { Label = "middle", Child = new Node { Label = "leaf" } }
    }.ToByteArray();

    #region ->> Draft 4 — the control <<-

    [Test]
    public async ValueTask draft_04_validates_a_transcoded_payload() {
        // Arrange
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, OrderType, Draft04);

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(ProbeSchema.Write(OrderPayload()));

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    #endregion // Draft 4 — the control

    #region ->> Draft 2020-12 <<-

    [Test]
    public async ValueTask the_validator_parses_a_2020_12_document() {
        // Arrange — reference resolution is lazy, so an unreadable document throws here rather than
        // returning errors. This is the step the report said blew up.
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, OrderType, Draft202012);

        // Act
        var act = () => NJsonSchemaAgent.Instance.ParseSchema(exported).Validate("{}");

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async ValueTask draft_2020_12_validates_a_transcoded_payload() {
        // Arrange — the root is a $ref into $defs under this dialect, which is the shape draft 4 avoids.
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, OrderType, Draft202012);

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(ProbeSchema.Write(OrderPayload()));

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask draft_2020_12_resolves_a_self_referencing_type() {
        // Arrange — a root $ref whose target references itself. Named in the report as the case whose
        // resolver could not follow it, and the reason every recursive message forced draft 4.
        var exported = ProtobufJsonSchema.Export(ProbeSchema.Set, NodeType, Draft202012);

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(ProbeSchema.Write(NodePayload(), NodeType));

        // Assert
        await Assert.That(result.IsValid).IsTrue().Because(Errors(result));
    }

    [Test]
    public async ValueTask draft_2020_12_rejects_a_payload_the_schema_forbids() {
        // Arrange — a passing validation proves nothing if the validator accepts everything. The schema
        // closes its objects, so an undeclared property has to be rejected.
        var exported   = ProtobufJsonSchema.Export(ProbeSchema.Set, OrderType, Draft202012);
        var transcoded = ProbeSchema.Write(OrderPayload()).TrimEnd('}') + ",\"notAField\":1}";

        // Act
        var result = NJsonSchemaAgent.Instance.ParseSchema(exported).Validate(transcoded);

        // Assert
        await Assert.That(result.IsValid).IsFalse();
    }

    #endregion // Draft 2020-12

    static string Errors(SchemaValidationResult result) =>
        string.Join("; ", result.Errors.Select(error => error.ToString()));
}
