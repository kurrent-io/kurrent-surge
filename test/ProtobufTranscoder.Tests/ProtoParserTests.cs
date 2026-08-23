using ProtobufTranscoder.Descriptors;

namespace ProtobufTranscoder.Tests;

public class ProtoParserTests {
    [Test]
    public async Task Parse_SimpleMessage_CreatesDescriptor() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Person {
              string name = 1;
              int32 age = 2;
            }
            """;

        var pool   = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var person = pool.FindMessageType("test.Person");

        await Assert.That(person.FullName).IsEqualTo("test.Person");
        await Assert.That(person.Fields).HasCount().EqualTo(2);
        await Assert.That(person.FindFieldByNumber(1)!.Name).IsEqualTo("name");
        await Assert.That(person.FindFieldByNumber(1)!.Type).IsEqualTo(FieldType.String);
        await Assert.That(person.FindFieldByNumber(2)!.Name).IsEqualTo("age");
        await Assert.That(person.FindFieldByNumber(2)!.Type).IsEqualTo(FieldType.Int32);
    }

    [Test]
    public async Task Parse_Enum_CreatesDescriptor() {
        const string proto = """
            syntax = "proto3";
            package test;
            enum Color {
              RED = 0;
              GREEN = 1;
              BLUE = 2;
            }
            message Paint {
              Color color = 1;
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var color = pool.FindEnumType("test.Color");

        await Assert.That(color.FullName).IsEqualTo("test.Color");
        await Assert.That(color.Values).HasCount().EqualTo(3);
        await Assert.That(color.FindValueByNumber(0)!.Name).IsEqualTo("RED");
        await Assert.That(color.FindValueByNumber(2)!.Name).IsEqualTo("BLUE");
    }

    [Test]
    public async Task Parse_NestedMessage_CreatesDescriptor() {
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

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var outer = pool.FindMessageType("test.Outer");
        var inner = pool.FindMessageType("test.Outer.Inner");

        await Assert.That(outer.FullName).IsEqualTo("test.Outer");
        await Assert.That(inner.FullName).IsEqualTo("test.Outer.Inner");
        await Assert.That(inner.Fields).HasCount().EqualTo(1);
    }

    [Test]
    public async Task Parse_RepeatedField() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Numbers {
              repeated int32 values = 1;
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Numbers");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.IsRepeated).IsTrue();
        await Assert.That(field.IsPacked).IsTrue();
    }

    [Test]
    public async Task Parse_MapField_CreatesSyntheticEntry() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Config {
              map<string, int32> settings = 1;
            }
            """;

        var pool  = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg   = pool.FindMessageType("test.Config");
        var field = msg.FindFieldByNumber(1)!;

        await Assert.That(field.IsMap).IsTrue();
        await Assert.That(field.IsRepeated).IsTrue();
    }

    [Test]
    public async Task Parse_OneofField() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Result {
              oneof value {
                string text = 1;
                int32 number = 2;
              }
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("test.Result");

        await Assert.That(msg.Oneofs).HasCount().EqualTo(1);
        await Assert.That(msg.Oneofs[0].Name).IsEqualTo("value");
        await Assert.That(msg.Oneofs[0].Fields).HasCount().EqualTo(2);

        var textField = msg.FindFieldByNumber(1)!;
        await Assert.That(textField.ContainingOneof).IsNotNull();
        await Assert.That(textField.ContainingOneof!.Name).IsEqualTo("value");
    }

    [Test]
    public async Task Parse_WellKnownTimestampImport() {
        const string proto = """
            syntax = "proto3";
            package test;
            import "google/protobuf/timestamp.proto";
            message Event {
              string name = 1;
              google.protobuf.Timestamp created_at = 2;
            }
            """;

        var pool      = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var timestamp = pool.FindMessageType("google.protobuf.Timestamp");

        await Assert.That(timestamp.FullName).IsEqualTo("google.protobuf.Timestamp");
        await Assert.That(timestamp.FindFieldByNumber(1)!.Name).IsEqualTo("seconds");
        await Assert.That(timestamp.FindFieldByNumber(2)!.Name).IsEqualTo("nanos");
    }

    [Test]
    public async Task Parse_NoPackage() {
        const string proto = """
            syntax = "proto3";
            message Simple {
              int32 id = 1;
            }
            """;

        var pool = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var msg  = pool.FindMessageType("Simple");
        await Assert.That(msg.FullName).IsEqualTo("Simple");
    }

    [Test]
    public async Task JsonName_ConvertsSnakeCaseToCamelCase() {
        await Assert.That(FieldDescriptor.ToJsonName("first_name")).IsEqualTo("firstName");
        await Assert.That(FieldDescriptor.ToJsonName("http_response_code")).IsEqualTo("httpResponseCode");
        await Assert.That(FieldDescriptor.ToJsonName("simple")).IsEqualTo("simple");
    }
}