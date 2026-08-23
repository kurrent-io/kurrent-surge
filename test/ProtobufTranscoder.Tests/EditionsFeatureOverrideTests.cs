using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Tests;

public class EditionsFeatureOverrideTests {
    [Test]
    public async Task Editions_MessageLevelFeatures() {
        const string proto = """
            edition = "2024";
            package test;
            message Foo {
              option features.field_presence = IMPLICIT;
              int32 a = 1;
              int32 b = 2 [features.field_presence = EXPLICIT];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Foo");

        // Message-level override to IMPLICIT should propagate to field a
        var aField = msg.FindFieldByNumber(1)!;
        await Assert.That(aField.Presence).IsEqualTo(FieldPresence.Implicit);

        // Field-level override back to EXPLICIT
        var bField = msg.FindFieldByNumber(2)!;
        await Assert.That(bField.Presence).IsEqualTo(FieldPresence.Explicit);
    }

    [Test]
    public async Task Editions_RepeatedFieldEncodingExpanded() {
        const string proto = """
            edition = "2024";
            package test;
            message Msg {
              repeated int32 packed_vals = 1;
              repeated int32 expanded_vals = 2 [features.repeated_field_encoding = EXPANDED];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        // Default for editions 2024 is PACKED
        var packedField = msg.FindFieldByNumber(1)!;
        await Assert.That(packedField.IsPacked).IsTrue();

        // Override to EXPANDED
        var expandedField = msg.FindFieldByNumber(2)!;
        await Assert.That(expandedField.IsPacked).IsFalse();
    }

    [Test]
    public async Task Editions_EnumTypeClosed() {
        const string proto = """
            edition = "2024";
            package test;
            enum OpenStatus {
              UNKNOWN = 0;
              ACTIVE = 1;
            }
            enum ClosedStatus {
              option features.enum_type = CLOSED;
              NONE = 0;
              DONE = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        var openEnum = pool.FindEnumType("test.OpenStatus");
        await Assert.That(openEnum.IsClosed).IsFalse();

        var closedEnum = pool.FindEnumType("test.ClosedStatus");
        await Assert.That(closedEnum.IsClosed).IsTrue();
    }

    [Test]
    public async Task Editions_LegacyRequired() {
        const string proto = """
            edition = "2024";
            package test;
            message Msg {
              int32 normal = 1;
              int32 required_field = 2 [features.field_presence = LEGACY_REQUIRED];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var normalField = msg.FindFieldByNumber(1)!;
        await Assert.That(normalField.Presence).IsEqualTo(FieldPresence.Explicit);

        var requiredField = msg.FindFieldByNumber(2)!;
        await Assert.That(requiredField.Presence).IsEqualTo(FieldPresence.Required);
    }

    [Test]
    public async Task Editions_FeatureInheritance_ThreeLevels() {
        // File → message → field inheritance chain
        const string proto = """
            edition = "2024";
            package test;
            option features.field_presence = IMPLICIT;
            message Outer {
              int32 a = 1;
              message Inner {
                int32 b = 1;
                int32 c = 2 [features.field_presence = EXPLICIT];
              }
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Field a inherits file-level IMPLICIT
        var outer  = pool.FindMessageType("test.Outer");
        var aField = outer.FindFieldByNumber(1)!;
        await Assert.That(aField.Presence).IsEqualTo(FieldPresence.Implicit);

        // Field b inherits file-level IMPLICIT through Outer
        var inner  = pool.FindMessageType("test.Outer.Inner");
        var bField = inner.FindFieldByNumber(1)!;
        await Assert.That(bField.Presence).IsEqualTo(FieldPresence.Implicit);

        // Field c overrides to EXPLICIT at field level
        var cField = inner.FindFieldByNumber(2)!;
        await Assert.That(cField.Presence).IsEqualTo(FieldPresence.Explicit);
    }

    [Test]
    public async Task Editions_NestedEnumInheritsMessageFeatures() {
        const string proto = """
            edition = "2024";
            package test;
            message Container {
              option features.enum_type = CLOSED;
              enum InnerStatus {
                UNKNOWN = 0;
                ACTIVE = 1;
              }
            }
            """;

        var pool      = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var innerEnum = pool.FindEnumType("test.Container.InnerStatus");
        await Assert.That(innerEnum.IsClosed).IsTrue();
    }
}