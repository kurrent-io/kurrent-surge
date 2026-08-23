using ProtobufTranscoder.Json;

namespace ProtobufTranscoder.Tests;

public class Stage2EndToEndTests {
    [Test]
    public async Task EndToEnd_Proto2_DecodeToJson() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Person {
              required string name = 1;
              optional int32 age = 2;
              optional string email = 3 [default = "none"];
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire: name = "Alice", age = 30 (email not set)
        byte[] data = [
            0x0A,
            0x05,
            0x41,
            0x6C,
            0x69,
            0x63,
            0x65, // name = "Alice"
            0x10,
            0x1E // age = 30
        ];

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Person");

        await Assert.That(node["name"]!.GetValue<string>()).IsEqualTo("Alice");
        await Assert.That(node["age"]!.GetValue<int>()).IsEqualTo(30);
        // email should NOT appear — explicit presence, not set
        await Assert.That(node["email"]).IsNull();
    }

    [Test]
    public async Task EndToEnd_Proto2_WithExtensions_DecodeToJson() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Event {
              required int32 id = 1;
              required string type = 2;
              extensions 100 to 199;
            }
            extend Event {
              optional string source = 100;
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire: id = 1, type = "click", source = "web"
        byte[] data = [
            0x08,
            0x01, // id = 1
            0x12,
            0x05,
            0x63,
            0x6C,
            0x69,
            0x63,
            0x6B, // type = "click"
            0xA2,
            0x06,
            0x03,
            0x77,
            0x65,
            0x62 // source = "web" (field 100)
        ];

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Event");

        await Assert.That(node["id"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(node["type"]!.GetValue<string>()).IsEqualTo("click");
        await Assert.That(node["source"]!.GetValue<string>()).IsEqualTo("web");
    }

    [Test]
    public async Task EndToEnd_Editions2024_DecodeToJson() {
        const string proto = """
            edition = "2024";
            package test;
            message Point {
              int32 x = 1;
              int32 y = 2;
              int32 z = 3 [features.field_presence = IMPLICIT];
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire: x = 10, y = 20 (z not set)
        byte[] data = [
            0x08,
            0x0A, // x = 10
            0x10,
            0x14 // y = 20
        ];

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Point");

        // x and y have explicit presence (editions 2024 default), set on wire
        await Assert.That(node["x"]!.GetValue<int>()).IsEqualTo(10);
        await Assert.That(node["y"]!.GetValue<int>()).IsEqualTo(20);

        // z has implicit presence, not on wire, should be omitted
        await Assert.That(node["z"]).IsNull();
    }

    [Test]
    public async Task EndToEnd_Proto2_EmitDefaults() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Config {
              optional int32 count = 1 [default = 42];
              optional bool enabled = 2;
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Empty message
        byte[] data = [];

        var node = ProtoTranscode.ToJsonNode(
            data, defs, "test.Config",
            new JsonSerializeOptions { EmitDefaults = true });

        // With EmitDefaults, unset fields should appear with their defaults
        await Assert.That(node["count"]).IsNotNull();
        await Assert.That(node["enabled"]).IsNotNull();
    }

    [Test]
    public async Task EndToEnd_Proto2_GroupEncodedMessage_ToJson() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Wrapper {
              required int32 id = 1;
              optional group Payload = 2 {
                optional string text = 1;
              }
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Wire: id = 1, StartGroup(2), text = "hi", EndGroup(2)
        // StartGroup for field 2: (2 << 3) | 3 = 0x13
        // EndGroup for field 2: (2 << 3) | 4 = 0x14
        byte[] data = [
            0x08,
            0x01, // id = 1
            0x13, // StartGroup field 2
            0x0A,
            0x02,
            0x68,
            0x69, // text = "hi"
            0x14  // EndGroup field 2
        ];

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Wrapper");

        await Assert.That(node["id"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(node["payload"]!["text"]!.GetValue<string>()).IsEqualTo("hi");
    }
}