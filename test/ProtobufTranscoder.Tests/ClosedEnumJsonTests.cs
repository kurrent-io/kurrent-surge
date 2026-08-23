using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Json;

namespace ProtobufTranscoder.Tests;

public class ClosedEnumJsonTests {
    [Test]
    public async Task ClosedEnum_UnknownValue_EmitsInteger() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Status {
              OK = 0;
              ERROR = 1;
            }
            message Msg {
              optional Status status = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire data: field 1, varint = 99 (not in enum definition)
        byte[] data = [0x08, 0x63]; // 0x63 = 99

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        // Unknown enum value should be emitted as integer
        await Assert.That(node["status"]!.GetValue<int>()).IsEqualTo(99);
    }

    [Test]
    public async Task ClosedEnum_KnownValue_EmitsName() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Status {
              OK = 0;
              ERROR = 1;
            }
            message Msg {
              optional Status status = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire data: field 1, varint = 1 (ERROR)
        byte[] data = [0x08, 0x01];

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        await Assert.That(node["status"]!.GetValue<string>()).IsEqualTo("ERROR");
    }

    [Test]
    public async Task ClosedEnum_UnknownValue_WithEnumsAsIntegers() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Status {
              OK = 0;
              ERROR = 1;
            }
            message Msg {
              optional Status status = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire data: field 1, varint = 42 (unknown)
        byte[] data = [0x08, 0x2A];

        var node = ProtoTranscode.ToJsonNode(
            data, pool, "test.Msg",
            new JsonSerializeOptions { EnumsAsIntegers = true });

        await Assert.That(node["status"]!.GetValue<int>()).IsEqualTo(42);
    }

    [Test]
    public async Task OpenEnum_UnknownValue_EmitsInteger() {
        const string proto = """
            syntax = "proto3";
            package test;
            enum Status {
              OK = 0;
              ERROR = 1;
            }
            message Msg {
              Status status = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire data: field 1, varint = 50 (unknown)
        byte[] data = [0x08, 0x32]; // 0x32 = 50

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        // Unknown value in open enum should be emitted as integer
        await Assert.That(node["status"]!.GetValue<int>()).IsEqualTo(50);
    }

    [Test]
    public async Task ClosedEnum_UnknownValue_DoesNotCorruptFollowingFields() {
        const string proto = """
            syntax = "proto2";
            package test;
            enum Color {
              RED = 0;
              GREEN = 1;
            }
            message Msg {
              optional Color color = 1;
              optional int32 value = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        // Wire data: color = 99 (unknown), value = 42
        byte[] data = [0x08, 0x63, 0x10, 0x2A]; // field 1 varint 99, field 2 varint 42

        var message    = DynamicMessage.Decode(descriptor, data);
        var valueField = descriptor.FindFieldByNumber(2)!;
        await Assert.That(message.GetField(valueField)).IsEqualTo(42);
    }
}