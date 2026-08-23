using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Wire;

namespace ProtobufTranscoder.Tests;

public class NestedGroupAndErrorTests {
    [Test]
    public async Task Decode_NestedGroups() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Root {
              optional group Outer = 1 {
                optional group Inner = 1 {
                  optional int32 value = 1;
                }
              }
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        var descriptor = pool.FindMessageType("test.Root");

        // Wire data:
        // StartGroup(1) for Outer
        //   StartGroup(1) for Inner
        //     field 1 varint = 7
        //   EndGroup(1) for Inner
        // EndGroup(1) for Outer
        byte[] data = [
            0x0B, // StartGroup field 1 (Outer)
            0x0B, // StartGroup field 1 (Inner)
            0x08,
            0x07, // field 1 varint = 7
            0x0C, // EndGroup field 1 (Inner)
            0x0C  // EndGroup field 1 (Outer)
        ];

        var message    = DynamicMessage.Decode(descriptor, data);
        var outerField = descriptor.FindFieldByNumber(1)!;
        var outerMsg   = message.GetField(outerField) as DynamicMessage;
        await Assert.That(outerMsg).IsNotNull();

        var innerField = outerMsg!.Descriptor.FindFieldByNumber(1)!;
        var innerMsg   = outerMsg.GetField(innerField) as DynamicMessage;
        await Assert.That(innerMsg).IsNotNull();

        var valueField = innerMsg!.Descriptor.FindFieldByNumber(1)!;
        await Assert.That(innerMsg.GetField(valueField)).IsEqualTo(7);
    }

    [Test]
    public async Task ReadGroup_MismatchedEndGroup_Throws() {
        // StartGroup for field 1, but EndGroup for field 2 — should hit EOF without finding match
        byte[] data = [
            0x08,
            0x2A, // field 1 varint = 42 (inside group)
            0x14  // EndGroup for field 2 (WRONG)
        ];

        DecodeException? caught = null;

        try {
            var reader = new CodedReader(data);
            _ = reader.ReadGroup(1); // expects EndGroup for field 1
        }
        catch (DecodeException ex) {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task ReadGroup_UnexpectedEOF_Throws() {
        // StartGroup for field 1, but no EndGroup at all
        byte[] data = [
            0x08, 0x2A // field 1 varint = 42 (inside group)
        ];

        DecodeException? caught = null;

        try {
            var reader = new CodedReader(data);
            _ = reader.ReadGroup(1);
        }
        catch (DecodeException ex) {
            caught = ex;
        }

        await Assert.That(caught).IsNotNull();
    }

    [Test]
    public async Task Decode_GroupWithSiblingFields() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 before = 1;
              optional group Payload = 2 {
                optional string text = 1;
              }
              optional int32 after = 3;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        // Wire: before=10, StartGroup(2), text="ab", EndGroup(2), after=20
        // StartGroup(2): (2<<3)|3 = 0x13, EndGroup(2): (2<<3)|4 = 0x14
        byte[] data = [
            0x08,
            0x0A, // before = 10
            0x13, // StartGroup field 2
            0x0A,
            0x02,
            0x61,
            0x62, // text = "ab"
            0x14, // EndGroup field 2
            0x18,
            0x14 // after = 20
        ];

        var message = DynamicMessage.Decode(descriptor, data);

        var beforeField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.GetField(beforeField)).IsEqualTo(10);

        var groupField = descriptor.FindFieldByNumber(2)!;
        var groupMsg   = message.GetField(groupField) as DynamicMessage;
        await Assert.That(groupMsg).IsNotNull();
        var textField = groupMsg!.Descriptor.FindFieldByNumber(1)!;
        await Assert.That(groupMsg.GetField(textField)).IsEqualTo("ab");

        var afterField = descriptor.FindFieldByNumber(3)!;
        await Assert.That(message.GetField(afterField)).IsEqualTo(20);
    }
}