using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Json;

namespace ProtobufTranscoder.Tests;

public class RequiredFieldTests {
    [Test]
    public async Task Required_HasField_WhenPresent() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              required int32 id = 1;
              required string name = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        byte[] data = [
            0x08,
            0x01, // id = 1
            0x12,
            0x03,
            0x66,
            0x6F,
            0x6F // name = "foo"
        ];

        var message   = DynamicMessage.Decode(descriptor, data);
        var idField   = descriptor.FindFieldByNumber(1)!;
        var nameField = descriptor.FindFieldByNumber(2)!;

        await Assert.That(idField.Presence).IsEqualTo(FieldPresence.Required);
        await Assert.That(nameField.Presence).IsEqualTo(FieldPresence.Required);
        await Assert.That(message.HasField(idField)).IsTrue();
        await Assert.That(message.HasField(nameField)).IsTrue();
    }

    [Test]
    public async Task Required_MissingField_LenientDecode() {
        // Proto2 required fields that are missing should still decode
        // (lenient behavior matching Java DynamicMessage)
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              required int32 id = 1;
              required string name = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        // Only id is present, name is missing
        byte[] data = [0x08, 0x05]; // id = 5

        // Should NOT throw — lenient decode
        var message = DynamicMessage.Decode(descriptor, data);

        var idField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.HasField(idField)).IsTrue();
        await Assert.That(message.GetField(idField)).IsEqualTo(5);

        var nameField = descriptor.FindFieldByNumber(2)!;
        await Assert.That(message.HasField(nameField)).IsFalse();
    }

    [Test]
    public async Task Required_EmitInJson_WhenSet() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              required int32 id = 1;
              optional string label = 2;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        byte[] data = [0x08, 0x07]; // id = 7

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        // Required field id is set — should appear
        await Assert.That(node["id"]!.GetValue<int>()).IsEqualTo(7);
        // Optional field label not set — should NOT appear
        await Assert.That(node["label"]).IsNull();
    }

    [Test]
    public async Task Required_NotEmittedInJson_WhenNotSet() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              required int32 id = 1;
              required string name = 2;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Empty wire data — no fields set (partial message)
        byte[] data = [];

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        // Neither required field was seen on the wire
        await Assert.That(node.AsObject().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Required_EmitDefaults_ShowsAll() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              required int32 id = 1;
              required string name = 2;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        byte[] data = [];

        var node = ProtoTranscode.ToJsonNode(
            data, pool, "test.Msg",
            new JsonSerializeOptions { EmitDefaults = true });

        // EmitDefaults should force both required fields to appear
        await Assert.That(node["id"]).IsNotNull();
        await Assert.That(node["name"]).IsNotNull();
    }
}