using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Tests;

public class Proto2ParserTests {
    [Test]
    public async Task Parse_Proto2Syntax_WithRequiredAndOptional() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Person {
              required string name = 1;
              optional int32 age = 2;
              optional string email = 3 [default = "unknown"];
            }
            """;

        var pool   = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var person = pool.FindMessageType("test.Person");

        await Assert.That(person.Fields).Count().IsEqualTo(3);

        var nameField = person.FindFieldByNumber(1)!;
        await Assert.That(nameField.Label).IsEqualTo(FieldLabel.Required);
        await Assert.That(nameField.Presence).IsEqualTo(FieldPresence.Required);

        var ageField = person.FindFieldByNumber(2)!;
        await Assert.That(ageField.Label).IsEqualTo(FieldLabel.Optional);
        await Assert.That(ageField.Presence).IsEqualTo(FieldPresence.Explicit);

        var emailField = person.FindFieldByNumber(3)!;
        await Assert.That(emailField.HasExplicitDefault).IsTrue();
        await Assert.That(emailField.DefaultValue).IsEqualTo("unknown");
    }

    [Test]
    public async Task Parse_Proto2DefaultValues() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Config {
              optional int32 count = 1 [default = 42];
              optional bool enabled = 2 [default = true];
              optional float ratio = 3 [default = 3.14];
              optional string label = 4 [default = "hello"];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Config");

        var countField = msg.FindFieldByNumber(1)!;
        await Assert.That(countField.HasExplicitDefault).IsTrue();
        await Assert.That(countField.DefaultValue).IsEqualTo(42);

        var enabledField = msg.FindFieldByNumber(2)!;
        await Assert.That(enabledField.DefaultValue).IsEqualTo(true);

        var ratioField = msg.FindFieldByNumber(3)!;
        await Assert.That(ratioField.HasExplicitDefault).IsTrue();

        var labelField = msg.FindFieldByNumber(4)!;
        await Assert.That(labelField.DefaultValue).IsEqualTo("hello");
    }

    [Test]
    public async Task Parse_Proto2ExtensionsRange() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Extensible {
              required int32 id = 1;
              extensions 100 to 199;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Extensible");

        await Assert.That(msg.ExtensionRanges).HasCount().EqualTo(1);
        await Assert.That(msg.ExtensionRanges[0].Start).IsEqualTo(100);
        await Assert.That(msg.ExtensionRanges[0].End).IsEqualTo(199);
    }

    [Test]
    public async Task Parse_Proto2ExtendBlock() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Extensible {
              required int32 id = 1;
              extensions 100 to 199;
            }
            extend Extensible {
              optional string ext_name = 100;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Extensible");

        // Extension should be findable via field number
        var extField = msg.FindFieldByNumber(100);
        await Assert.That(extField).IsNotNull();
        await Assert.That(extField!.Name).IsEqualTo("ext_name");
        await Assert.That(extField.Type).IsEqualTo(FieldType.String);
    }

    [Test]
    public async Task Parse_Proto2GroupField() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Outer {
              optional group MyGroup = 1 {
                optional int32 value = 1;
              }
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var outer = pool.FindMessageType("test.Outer");

        // Group creates a nested message
        var groupMsg = pool.FindMessageType("test.Outer.MyGroup");
        await Assert.That(groupMsg).IsNotNull();
        await Assert.That(groupMsg.Fields).HasCount().EqualTo(1);

        // Group creates a field referencing the nested message
        var groupField = outer.FindFieldByNumber(1)!;
        await Assert.That(groupField.Name).IsEqualTo("mygroup");
        await Assert.That(groupField.IsGroupEncoded).IsTrue();
        await Assert.That(groupField.Type).IsEqualTo(FieldType.Group);
    }

    [Test]
    public async Task Parse_Proto2RepeatedNotPackedByDefault() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Numbers {
              repeated int32 values = 1;
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Numbers");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.IsRepeated).IsTrue();
        await Assert.That(field.IsPacked).IsFalse();
    }

    [Test]
    public async Task Parse_Proto2RepeatedPackedExplicit() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Numbers {
              repeated int32 values = 1 [packed = true];
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Numbers");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.IsPacked).IsTrue();
    }

    [Test]
    public async Task Parse_Proto2ClosedEnum() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Status {
              UNKNOWN = 0;
              ACTIVE = 1;
            }
            """;

        var pool     = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var enumDesc = pool.FindEnumType("test.Status");

        await Assert.That(enumDesc.IsClosed).IsTrue();
    }

    [Test]
    public async Task Parse_Proto3OpenEnum() {
        const string proto = """
            syntax = "proto3";
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

    [Test]
    public async Task Parse_Proto2EnumDefaultValue() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Color {
              RED = 0;
              GREEN = 1;
              BLUE = 2;
            }
            message Paint {
              optional Color color = 1 [default = BLUE];
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Paint");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.HasExplicitDefault).IsTrue();
        var defaultVal = field.DefaultValue as EnumValueDescriptor;
        await Assert.That(defaultVal).IsNotNull();
        await Assert.That(defaultVal!.Name).IsEqualTo("BLUE");
    }
}