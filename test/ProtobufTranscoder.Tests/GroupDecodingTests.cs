using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Wire;

namespace ProtobufTranscoder.Tests;

public class GroupDecodingTests {
    [Test]
    public async Task ReadGroup_DecodesGroupWireFormat() {
        // Group wire format:
        // StartGroup tag (field 1, wire type 3) = (1 << 3) | 3 = 0x0B
        // Inner fields...
        // EndGroup tag (field 1, wire type 4) = (1 << 3) | 4 = 0x0C
        byte[] data = [
            0x0B, // StartGroup for field 1
            0x08,
            0x2A, // field 1 (varint) = 42 inside group
            0x0C  // EndGroup for field 1
        ];

        // ref struct can't cross await — collect values first
        int      fieldNumber;
        WireType wireType;
        int      innerFieldNumber;
        int      innerValue;
        bool     groupAtEnd;
        bool     readerAtEnd;

        var reader = new CodedReader(data);
        var tag    = reader.ReadTag();
        fieldNumber = CodedReader.GetFieldNumber(tag);
        wireType    = CodedReader.GetWireType(tag);

        var groupReader = reader.ReadGroup(fieldNumber);
        var innerTag    = groupReader.ReadTag();
        innerFieldNumber = CodedReader.GetFieldNumber(innerTag);
        innerValue       = groupReader.ReadInt32();
        groupAtEnd       = groupReader.IsAtEnd;
        readerAtEnd      = reader.IsAtEnd;

        await Assert.That(fieldNumber).IsEqualTo(1);
        await Assert.That(wireType).IsEqualTo(WireType.StartGroup);
        await Assert.That(innerFieldNumber).IsEqualTo(1);
        await Assert.That(innerValue).IsEqualTo(42);
        await Assert.That(groupAtEnd).IsTrue();
        await Assert.That(readerAtEnd).IsTrue();
    }

    [Test]
    public async Task Decode_GroupEncodedMessage() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Outer {
              optional group MyGroup = 1 {
                optional int32 value = 1;
              }
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        pool.LinkFieldTypes(GetParsedFiles(proto));

        var descriptor = pool.FindMessageType("test.Outer");

        // Wire data: StartGroup(field 1), inner field 1 = 42, EndGroup(field 1)
        byte[] data = [
            0x0B, // StartGroup for field 1
            0x08,
            0x2A, // inner field 1 (varint) = 42
            0x0C  // EndGroup for field 1
        ];

        var message    = DynamicMessage.Decode(descriptor, data);
        var groupField = descriptor.FindFieldByNumber(1)!;
        var groupMsg   = message.GetField(groupField) as DynamicMessage;

        await Assert.That(groupMsg).IsNotNull();
        var valueField = groupMsg!.Descriptor.FindFieldByNumber(1)!;
        await Assert.That(groupMsg.GetField(valueField)).IsEqualTo(42);
    }

    [Test]
    public async Task Decode_GroupWithMultipleFields() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Container {
              optional group Item = 1 {
                optional string name = 1;
                optional int32 count = 2;
              }
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        pool.LinkFieldTypes(GetParsedFiles(proto));

        var descriptor = pool.FindMessageType("test.Container");

        // Wire data:
        // StartGroup(1), string name="hello" (tag=0x0A, len=5, "hello"), int32 count=10 (tag=0x10, val=10), EndGroup(1)
        byte[] data = [
            0x0B, // StartGroup field 1
            0x0A,
            0x05,
            0x68,
            0x65,
            0x6C,
            0x6C,
            0x6F, // name = "hello"
            0x10,
            0x0A, // count = 10
            0x0C  // EndGroup field 1
        ];

        var message    = DynamicMessage.Decode(descriptor, data);
        var groupField = descriptor.FindFieldByNumber(1)!;
        var groupMsg   = message.GetField(groupField) as DynamicMessage;

        await Assert.That(groupMsg).IsNotNull();
        var nameField  = groupMsg!.Descriptor.FindFieldByNumber(1)!;
        var countField = groupMsg.Descriptor.FindFieldByNumber(2)!;
        await Assert.That(groupMsg.GetField(nameField)).IsEqualTo("hello");
        await Assert.That(groupMsg.GetField(countField)).IsEqualTo(10);
    }

    static IEnumerable<Parser.FileDescriptorProto> GetParsedFiles(string proto) => [Parser.ProtoParser.Parse("test.proto", proto)];
}