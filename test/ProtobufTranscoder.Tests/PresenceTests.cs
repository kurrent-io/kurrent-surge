using ProtobufTranscoder.Dynamic;

namespace ProtobufTranscoder.Tests;

public class PresenceTests {
    [Test]
    public async Task HasField_TracksBitset_ForExplicitPresence() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 a = 1;
              optional int32 b = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        // Only field a = 0 is set (value is the default, but it's present on the wire)
        byte[] data = [0x08, 0x00]; // field 1, varint = 0

        var message = DynamicMessage.Decode(descriptor, data);
        var aField  = descriptor.FindFieldByNumber(1)!;
        var bField  = descriptor.FindFieldByNumber(2)!;

        await Assert.That(message.HasField(aField)).IsTrue();
        await Assert.That(message.HasField(bField)).IsFalse();
    }

    [Test]
    public async Task Proto2_JsonEmission_OnlyEmitsSetFields() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 a = 1;
              optional int32 b = 2;
              optional string c = 3;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // Only field a = 42 is on the wire
        byte[] data = [0x08, 0x2A]; // field 1, varint = 42

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        await Assert.That(node["a"]!.GetValue<int>()).IsEqualTo(42);

        // b and c should NOT be in the output (explicit presence, not set)
        await Assert.That(node["b"]).IsNull();
        await Assert.That(node["c"]).IsNull();
    }

    [Test]
    public async Task Proto3_JsonEmission_OmitsDefaultValues() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Msg {
              int32 a = 1;
              string b = 2;
              bool c = 3;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));

        // All fields with zero/default values
        byte[] data = [0x08, 0x00]; // field 1, varint = 0

        var node = ProtoTranscode.ToJsonNode(data, pool, "test.Msg");

        // Proto3 implicit presence: default values are omitted
        await Assert.That(node.AsObject().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Proto2_DefaultValues_ReturnedForUnsetFields() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Config {
              optional int32 count = 1 [default = 42];
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Config");

        // Empty wire data — no fields set
        byte[] data    = [];
        var    message = DynamicMessage.Decode(descriptor, data);

        var countField = descriptor.FindFieldByNumber(1)!;
        await Assert.That(message.HasField(countField)).IsFalse();
        await Assert.That(message.GetField(countField)).IsEqualTo(42); // Returns default
    }

    [Test]
    public async Task Proto2_SetToDefault_StillHasField() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 value = 1 [default = 42];
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");

        // Field set to its default value of 42
        byte[] data = [0x08, 0x2A]; // field 1, varint = 42

        var message = DynamicMessage.Decode(descriptor, data);
        var field   = descriptor.FindFieldByNumber(1)!;

        await Assert.That(message.HasField(field)).IsTrue();
        await Assert.That(message.GetField(field)).IsEqualTo(42);
    }

    [Test]
    public async Task Builder_TracksPresence() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 a = 1;
              optional int32 b = 2;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Msg");
        var aField     = descriptor.FindFieldByNumber(1)!;
        var bField     = descriptor.FindFieldByNumber(2)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(aField, 10);
        var message = builder.Build();

        await Assert.That(message.HasField(aField)).IsTrue();
        await Assert.That(message.HasField(bField)).IsFalse();
    }
}