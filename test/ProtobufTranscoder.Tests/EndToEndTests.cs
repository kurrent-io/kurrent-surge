namespace ProtobufTranscoder.Tests;

public class EndToEndTests {
    [Test]
    public async Task EndToEnd_DecodeToJson_SimpleMessage() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Person {
              string name = 1;
              int32 age = 2;
              bool active = 3;
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // { name: "Bob", age: 25, active: true }
        byte[] data = [
            0x0A,
            0x03,
            0x42,
            0x6F,
            0x62, // name = "Bob"
            0x10,
            0x19, // age = 25
            0x18,
            0x01 // active = true
        ];

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Person");

        await Assert.That(node["name"]!.GetValue<string>()).IsEqualTo("Bob");
        await Assert.That(node["age"]!.GetValue<int>()).IsEqualTo(25);
        await Assert.That(node["active"]!.GetValue<bool>()).IsTrue();
    }

    [Test]
    public async Task EndToEnd_DecodeToJson_EmptyMessage() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Empty {}
            """;

        var    defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        byte[] data = []; // empty message

        var node = ProtoTranscode.ToJsonNode(data, defs, "test.Empty");

        await Assert.That(node.AsObject().Count).IsEqualTo(0);
    }

    [Test]
    public async Task EndToEnd_DecodeToJson_PackedRepeated() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Scores {
              repeated int32 scores = 1;
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // packed repeated: tag=0x0A, len=3, values: 10, 20, 30
        byte[] data = [0x0A, 0x03, 0x0A, 0x14, 0x1E];

        var node   = ProtoTranscode.ToJsonNode(data, defs, "test.Scores");
        var scores = node["scores"]!.AsArray();

        await Assert.That(scores.Count).IsEqualTo(3);
        await Assert.That(scores[0]!.GetValue<int>()).IsEqualTo(10);
        await Assert.That(scores[1]!.GetValue<int>()).IsEqualTo(20);
        await Assert.That(scores[2]!.GetValue<int>()).IsEqualTo(30);
    }

    [Test]
    public void UnknownMessageType_ThrowsArgumentException() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Known {}
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        Assert.Throws<ArgumentException>(action: () => ProtoTranscode.ToDynamicMessage([], defs, "test.Unknown"));
    }

    [Test]
    public async Task EndToEnd_Decode_ReturnsModel() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Point {
              double x = 1;
              double y = 2;
            }
            """;

        var defs = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // x = 1.5, y = 2.5
        // field 1 (double): tag=0x09, 8 bytes LE
        // field 2 (double): tag=0x11, 8 bytes LE
        byte[] data = [0x09, ..BitConverter.GetBytes(1.5), 0x11, ..BitConverter.GetBytes(2.5)];

        var message = ProtoTranscode.ToDynamicMessage(data, defs, "test.Point");
        var xField  = message.Descriptor.FindFieldByNumber(1)!;
        var yField  = message.Descriptor.FindFieldByNumber(2)!;

        await Assert.That(message.GetField(xField)).IsEqualTo(1.5);
        await Assert.That(message.GetField(yField)).IsEqualTo(2.5);
    }

    [Test]
    public async Task EndToEnd_MultipleProtoFiles() {
        const string addressProto = """
            syntax = "proto3";
            package test;
            message Address {
              string city = 1;
            }
            """;

        const string personProto = """
            syntax = "proto3";
            package test;
            message Employee {
              string name = 1;
            }
            """;

        var defs = ProtoDefinitions.Build(
            new ProtoSchema("address.proto", addressProto),
            new ProtoSchema("person.proto", personProto));

        // Can decode both types
        var address  = defs.FindMessageType("test.Address");
        var employee = defs.FindMessageType("test.Employee");

        await Assert.That(address.FullName).IsEqualTo("test.Address");
        await Assert.That(employee.FullName).IsEqualTo("test.Employee");
    }
}