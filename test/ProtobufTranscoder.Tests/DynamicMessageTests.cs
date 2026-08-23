using ProtobufTranscoder.Dynamic;

namespace ProtobufTranscoder.Tests;

public class DynamicMessageTests {
    [Test]
    public async Task Decode_SimpleMessage_WithInt32AndString() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Person {
              string name = 1;
              int32 age = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Person");

        // Protobuf bytes for { name: "Alice", age: 30 }
        // field 1 (string): tag=0x0A, len=5, "Alice"
        // field 2 (int32):  tag=0x10, value=30
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

        var message = DynamicMessage.Decode(descriptor, data);

        var nameField = descriptor.FindFieldByNumber(1)!;
        var ageField  = descriptor.FindFieldByNumber(2)!;

        await Assert.That(message.GetField(nameField)).IsEqualTo("Alice");
        await Assert.That(message.GetField(ageField)).IsEqualTo(30);
    }

    [Test]
    public async Task Decode_RepeatedInt32_Packed() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Numbers {
              repeated int32 values = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Numbers");

        // Packed repeated: tag=0x0A (field 1, LEN), length=3, values: 1, 2, 3
        byte[] data = [0x0A, 0x03, 0x01, 0x02, 0x03];

        var message     = DynamicMessage.Decode(descriptor, data);
        var valuesField = descriptor.FindFieldByNumber(1)!;
        var values      = message.GetField(valuesField) as List<object>;

        await Assert.That(values).IsNotNull();
        await Assert.That(values!).HasCount().EqualTo(3);
        await Assert.That(values[0]).IsEqualTo(1);
        await Assert.That(values[1]).IsEqualTo(2);
        await Assert.That(values[2]).IsEqualTo(3);
    }

    [Test]
    public async Task Decode_NestedMessage() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Outer {
              message Inner {
                int32 value = 1;
              }
              Inner inner = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        pool.LinkFieldTypes(GetParsedFiles(proto));

        var descriptor = pool.FindMessageType("test.Outer");

        // inner = { value: 42 }
        // inner field: tag=0x0A, length=2, [tag=0x08, value=42]
        byte[] data = [0x0A, 0x02, 0x08, 0x2A];

        var message    = DynamicMessage.Decode(descriptor, data);
        var innerField = descriptor.FindFieldByNumber(1)!;
        var innerMsg   = message.GetField(innerField) as DynamicMessage;

        await Assert.That(innerMsg).IsNotNull();
        var valueField = innerMsg!.Descriptor.FindFieldByNumber(1)!;
        await Assert.That(innerMsg.GetField(valueField)).IsEqualTo(42);
    }

    [Test]
    public async Task Decode_BoolField() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Flags {
              bool active = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Flags");

        // active = true: tag=0x08, value=0x01
        byte[] data = [0x08, 0x01];

        var message     = DynamicMessage.Decode(descriptor, data);
        var activeField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(activeField)).IsEqualTo(true);
    }

    [Test]
    public async Task Decode_UnknownField_IsSkipped() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Known {
              int32 id = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Known");

        // field 1 = 42, field 99 (unknown) varint = 1
        // tag1=0x08, val=42, tag99=(99<<3|0)=0xF8 0x06, val=1
        byte[] data = [0x08, 0x2A, 0xF8, 0x06, 0x01];

        var message = DynamicMessage.Decode(descriptor, data);
        var idField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(idField)).IsEqualTo(42);
    }

    [Test]
    public async Task Builder_CanConstructMessageProgrammatically() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Item {
              string name = 1;
              int32 quantity = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Item");
        var nameField  = descriptor.FindFieldByNumber(1)!;
        var qtyField   = descriptor.FindFieldByNumber(2)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(nameField, "Widget");
        builder.SetField(qtyField, 5);
        var message = builder.Build();

        await Assert.That(message.GetField(nameField)).IsEqualTo("Widget");
        await Assert.That(message.GetField(qtyField)).IsEqualTo(5);
    }

    static IEnumerable<Parser.FileDescriptorProto> GetParsedFiles(string proto) => [Parser.ProtoParser.Parse("test.proto", proto)];
}