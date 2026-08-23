using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Dynamic;

namespace ProtobufTranscoder.Tests;

public class ExtensionTests {
    [Test]
    public async Task Extension_FindFieldByNumber_ReturnsExtensionField() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
            }
            extend Base {
              optional string tag = 100;
              optional int32 priority = 101;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Base");

        var idField = msg.FindFieldByNumber(1);
        await Assert.That(idField).IsNotNull();
        await Assert.That(idField!.Name).IsEqualTo("id");

        var tagField = msg.FindFieldByNumber(100);
        await Assert.That(tagField).IsNotNull();
        await Assert.That(tagField!.Name).IsEqualTo("tag");
        await Assert.That(tagField.Type).IsEqualTo(FieldType.String);

        var priorityField = msg.FindFieldByNumber(101);
        await Assert.That(priorityField).IsNotNull();
        await Assert.That(priorityField!.Name).IsEqualTo("priority");
    }

    [Test]
    public async Task Extension_Decode_WithExtensionField() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
            }
            extend Base {
              optional string tag = 100;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Base");

        // Wire data: id = 1, tag = "hello"
        // field 1 (varint) = 1: 0x08, 0x01
        // field 100 (LEN) = "hello": tag = (100 << 3 | 2) = 0xA0 0x06, len=5, "hello"
        byte[] data = [
            0x08,
            0x01, // id = 1
            0xA2,
            0x06,
            0x05,
            0x68,
            0x65,
            0x6C,
            0x6C,
            0x6F // tag = "hello"
        ];

        var message = DynamicMessage.Decode(descriptor, data);

        var idField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(idField)).IsEqualTo(1);

        var tagField = descriptor.FindFieldByNumber(100)!;
        await Assert.That(message.GetField(tagField)).IsEqualTo("hello");
    }

    [Test]
    public async Task Extension_UnknownFieldInExtensionRange_IsSkipped() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
            }
            """;

        // No extend block — field 100 is in range but not registered
        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Base");

        // Wire data: id = 1, unknown field 100 (varint) = 42
        byte[] data = [
            0x08,
            0x01, // id = 1
            0xA0,
            0x06,
            0x2A // field 100 (varint) = 42 — unknown extension
        ];

        // Should not throw; unknown extension is skipped
        var message = DynamicMessage.Decode(descriptor, data);
        var idField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(idField)).IsEqualTo(1);
    }
}