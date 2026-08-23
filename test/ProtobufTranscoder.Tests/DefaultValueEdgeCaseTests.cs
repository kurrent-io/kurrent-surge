namespace ProtobufTranscoder.Tests;

public class DefaultValueEdgeCaseTests {
    [Test]
    public async Task Parse_NegativeIntDefault() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 x = 1 [default = -1];
              optional int64 y = 2 [default = -999];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var xField = msg.FindFieldByNumber(1)!;
        await Assert.That(xField.HasExplicitDefault).IsTrue();
        await Assert.That(xField.DefaultValue).IsEqualTo(-1);

        var yField = msg.FindFieldByNumber(2)!;
        await Assert.That(yField.HasExplicitDefault).IsTrue();
        await Assert.That(yField.DefaultValue).IsEqualTo(-999L);
    }

    [Test]
    public async Task Parse_DoubleDefault() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional double pi = 1 [default = 3.14159];
              optional double neg = 2 [default = -2.718];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var piField = msg.FindFieldByNumber(1)!;
        await Assert.That(piField.HasExplicitDefault).IsTrue();
        var piDefault = (double)piField.DefaultValue!;
        await Assert.That(Math.Abs(piDefault - 3.14159)).IsLessThan(0.0001);

        var negField = msg.FindFieldByNumber(2)!;
        await Assert.That(negField.HasExplicitDefault).IsTrue();
        var negDefault = (double)negField.DefaultValue!;
        await Assert.That(Math.Abs(negDefault - -2.718)).IsLessThan(0.001);
    }

    [Test]
    public async Task Parse_FloatSpecialValues() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional float pos_inf = 1 [default = inf];
              optional float neg_inf = 2 [default = -inf];
              optional float not_a_number = 3 [default = nan];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var posInf = (float)msg.FindFieldByNumber(1)!.DefaultValue!;
        await Assert.That(float.IsPositiveInfinity(posInf)).IsTrue();

        var negInf = (float)msg.FindFieldByNumber(2)!.DefaultValue!;
        await Assert.That(float.IsNegativeInfinity(negInf)).IsTrue();

        var nan = (float)msg.FindFieldByNumber(3)!.DefaultValue!;
        await Assert.That(float.IsNaN(nan)).IsTrue();
    }

    [Test]
    public async Task Parse_BytesDefault() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional bytes data = 1 [default = "\x01\x02\xff"];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var field = msg.FindFieldByNumber(1)!;
        await Assert.That(field.HasExplicitDefault).IsTrue();
        var bytes = field.DefaultValue as byte[];
        await Assert.That(bytes).IsNotNull();
        await Assert.That(bytes![0]).IsEqualTo((byte)0x01);
        await Assert.That(bytes[1]).IsEqualTo((byte)0x02);
        await Assert.That(bytes[2]).IsEqualTo((byte)0xFF);
    }

    [Test]
    public async Task Parse_UInt32Default() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional uint32 count = 1 [default = 100];
              optional uint64 big = 2 [default = 999999];
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        var countField = msg.FindFieldByNumber(1)!;
        await Assert.That(countField.DefaultValue).IsEqualTo(100u);

        var bigField = msg.FindFieldByNumber(2)!;
        await Assert.That(bigField.DefaultValue).IsEqualTo(999999uL);
    }

    [Test]
    public async Task Proto2_ZeroDefaults_WhenNoExplicitDefault() {
        const string proto = """
            syntax = "proto2";
            package test;
            message Msg {
              optional int32 a = 1;
              optional string b = 2;
              optional bool c = 3;
              optional float d = 4;
              optional double e = 5;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Msg");

        await Assert.That(msg.FindFieldByNumber(1)!.DefaultValue).IsEqualTo(0);
        await Assert.That(msg.FindFieldByNumber(2)!.DefaultValue).IsEqualTo("");
        await Assert.That(msg.FindFieldByNumber(3)!.DefaultValue).IsEqualTo(false);
        await Assert.That(msg.FindFieldByNumber(4)!.DefaultValue).IsEqualTo(0.0f);
        await Assert.That(msg.FindFieldByNumber(5)!.DefaultValue).IsEqualTo(0.0);

        await Assert.That(msg.FindFieldByNumber(1)!.HasExplicitDefault).IsFalse();
    }
}