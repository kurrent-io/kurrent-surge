using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Tests;

public class EditionsParserTests {
    [Test]
    public async Task Parse_EditionsSyntax() {
        const string proto = """
            edition = "2024";
            package test;
            message Simple {
              int32 id = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Simple");

        await Assert.That(msg).IsNotNull();
        await Assert.That(msg.Fields).HasCount().EqualTo(1);
    }

    [Test]
    public async Task Parse_Editions_FieldPresenceOverride() {
        const string proto = """
            edition = "2024";
            package test;
            message Foo {
              int32 x = 1 [features.field_presence = IMPLICIT];
              int32 y = 2;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Foo");

        var xField = msg.FindFieldByNumber(1)!;
        await Assert.That(xField.Presence).IsEqualTo(FieldPresence.Implicit);

        // Default for editions 2024 is EXPLICIT
        var yField = msg.FindFieldByNumber(2)!;
        await Assert.That(yField.Presence).IsEqualTo(FieldPresence.Explicit);
    }

    [Test]
    public async Task Parse_Editions_FileLevel_FieldPresence() {
        const string proto = """
            edition = "2024";
            package test;
            option features.field_presence = IMPLICIT;
            message Bar {
              int32 a = 1;
              int32 b = 2 [features.field_presence = EXPLICIT];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Bar");

        var aField = msg.FindFieldByNumber(1)!;
        await Assert.That(aField.Presence).IsEqualTo(FieldPresence.Implicit);

        var bField = msg.FindFieldByNumber(2)!;
        await Assert.That(bField.Presence).IsEqualTo(FieldPresence.Explicit);
    }

    [Test]
    public async Task Parse_Editions_RepeatedFieldsPackedByDefault() {
        const string proto = """
            edition = "2024";
            package test;
            message Numbers {
              repeated int32 values = 1;
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Numbers");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.IsPacked).IsTrue();
    }

    [Test]
    public async Task Parse_Editions_OpenEnumByDefault() {
        const string proto = """
            edition = "2024";
            package test;
            enum Status {
              UNKNOWN = 0;
              ACTIVE = 1;
            }
            """;

        var pool     = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var enumDesc = pool.FindEnumType("test.Status");

        await Assert.That(enumDesc.IsClosed).IsFalse();
    }
}