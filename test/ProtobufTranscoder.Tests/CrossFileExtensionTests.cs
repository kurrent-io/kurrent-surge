using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Dynamic;

namespace ProtobufTranscoder.Tests;

public class CrossFileExtensionTests {
    [Test]
    public async Task Extension_DefinedInSeparateFile() {
        const string baseProto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
            }
            """;

        const string extProto = """
            syntax = "proto2";
            package test;
            import "base.proto";
            extend Base {
              optional string label = 100;
            }
            """;

        var pool = ProtoDefinitions.Build(
            new ProtoSchema("base.proto", baseProto),
            new ProtoSchema("ext.proto", extProto));

        var msg = pool.FindMessageType("test.Base");

        // Extension from the other file should be resolved
        var labelField = msg.FindFieldByNumber(100);
        await Assert.That(labelField).IsNotNull();
        await Assert.That(labelField!.Name).IsEqualTo("label");
        await Assert.That(labelField.Type).IsEqualTo(FieldType.String);
    }

    [Test]
    public async Task Extension_CrossFile_Decode() {
        const string baseProto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
            }
            """;

        const string extProto = """
            syntax = "proto2";
            package test;
            import "base.proto";
            extend Base {
              optional int32 priority = 100;
              optional string tag = 101;
            }
            """;

        var pool = ProtoDefinitions.Build(
            new ProtoSchema("base.proto", baseProto),
            new ProtoSchema("ext.proto", extProto));

        var descriptor = pool.FindMessageType("test.Base");

        // Wire data: id=1, priority=5 (field 100, varint), tag="hi" (field 101, LEN)
        // field 100 varint: tag = (100 << 3 | 0) = 0xA0 0x06, value = 5
        // field 101 LEN: tag = (101 << 3 | 2) = 0xAA 0x06, len=2, "hi"
        byte[] data = [
            0x08,
            0x01, // id = 1
            0xA0,
            0x06,
            0x05, // priority = 5
            0xAA,
            0x06,
            0x02,
            0x68,
            0x69 // tag = "hi"
        ];

        var message = DynamicMessage.Decode(descriptor, data);

        var idField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(idField)).IsEqualTo(1);

        var priorityField = descriptor.FindFieldByNumber(100)!;
        await Assert.That(message.GetField(priorityField)).IsEqualTo(5);

        var tagField = descriptor.FindFieldByNumber(101)!;
        await Assert.That(message.GetField(tagField)).IsEqualTo("hi");
    }

    [Test]
    public async Task Extension_MultipleFiles_SameMessage() {
        const string baseProto = """
            syntax = "proto2";
            package test;
            message Base {
              required int32 id = 1;
              extensions 100 to 199;
              extensions 200 to 299;
            }
            """;

        const string ext1Proto = """
            syntax = "proto2";
            package test;
            import "base.proto";
            extend Base {
              optional string name = 100;
            }
            """;

        const string ext2Proto = """
            syntax = "proto2";
            package test;
            import "base.proto";
            extend Base {
              optional int32 age = 200;
            }
            """;

        var pool = ProtoDefinitions.Build(
            new ProtoSchema("base.proto", baseProto),
            new ProtoSchema("ext1.proto", ext1Proto),
            new ProtoSchema("ext2.proto", ext2Proto));

        var msg = pool.FindMessageType("test.Base");

        var nameField = msg.FindFieldByNumber(100);
        await Assert.That(nameField).IsNotNull();
        await Assert.That(nameField!.Name).IsEqualTo("name");

        var ageField = msg.FindFieldByNumber(200);
        await Assert.That(ageField).IsNotNull();
        await Assert.That(ageField!.Name).IsEqualTo("age");
    }
}