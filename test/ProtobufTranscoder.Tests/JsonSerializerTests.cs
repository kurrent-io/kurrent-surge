using System.Text.Json;
using ProtobufTranscoder.Dynamic;
using ProtobufTranscoder.Json;

namespace ProtobufTranscoder.Tests;

public class JsonSerializerTests {
    [Test]
    public async Task Serialize_SimpleMessage_ToJson() {
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
        var nameField  = descriptor.FindFieldByNumber(1)!;
        var ageField   = descriptor.FindFieldByNumber(2)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(nameField, "Alice");
        builder.SetField(ageField, 30);
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));

        await Assert.That(doc.RootElement.GetProperty("name").GetString()).IsEqualTo("Alice");
        await Assert.That(doc.RootElement.GetProperty("age").GetInt32()).IsEqualTo(30);
    }

    [Test]
    public async Task Serialize_UseProtoFieldNames() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Item {
              string display_name = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Item");
        var field      = descriptor.FindFieldByNumber(1)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(field, "Test");
        var message = builder.Build();

        // Default: camelCase
        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));
        await Assert.That(doc.RootElement.TryGetProperty("displayName", out _)).IsTrue();

        // With proto names
        var       protoSerializer = new DynamicMessageJsonSerializer(new JsonSerializeOptions { UseProtoFieldNames = true });
        using var protoDoc        = JsonDocument.Parse(protoSerializer.Write(message));
        await Assert.That(protoDoc.RootElement.TryGetProperty("display_name", out _)).IsTrue();
    }

    [Test]
    public async Task Serialize_Int64_AsString() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Big {
              int64 value = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Big");
        var field      = descriptor.FindFieldByNumber(1)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(field, 9007199254740993L); // > Number.MAX_SAFE_INTEGER
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));

        // Int64 should be a string in proto JSON
        await Assert.That(doc.RootElement.GetProperty("value").GetString()).IsEqualTo("9007199254740993");
    }

    [Test]
    public async Task Serialize_RepeatedField_AsArray() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Tags {
              repeated string tags = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Tags");
        var field      = descriptor.FindFieldByNumber(1)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.AddRepeatedField(field, "alpha");
        builder.AddRepeatedField(field, "beta");
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));
        var       arr        = doc.RootElement.GetProperty("tags");

        await Assert.That(arr.GetArrayLength()).IsEqualTo(2);
        await Assert.That(arr[0].GetString()).IsEqualTo("alpha");
        await Assert.That(arr[1].GetString()).IsEqualTo("beta");
    }

    [Test]
    public async Task Serialize_BoolField() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Flags {
              bool active = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Flags");
        var field      = descriptor.FindFieldByNumber(1)!;

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(field, true);
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));
        await Assert.That(doc.RootElement.GetProperty("active").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task Serialize_BytesField_AsBase64() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Data {
              bytes payload = 1;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Data");
        var field      = descriptor.FindFieldByNumber(1)!;

        byte[] payload = [0x01, 0x02, 0x03];
        var    builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(field, payload);
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));
        await Assert.That(doc.RootElement.GetProperty("payload").GetString()).IsEqualTo(Convert.ToBase64String(payload));
    }

    [Test]
    public async Task Serialize_FloatSpecialValues() {
        const string proto = """
            syntax = "proto3";
            package test;
            message Floats {
              float nan_val = 1;
              float inf_val = 2;
              float neg_inf_val = 3;
            }
            """;

        var pool       = ProtoDefinitions.Build(new ProtoSchema("test.proto", proto));
        var descriptor = pool.FindMessageType("test.Floats");

        var builder = DynamicMessage.Builder.NewBuilder(descriptor);
        builder.SetField(descriptor.FindFieldByNumber(1)!, float.NaN);
        builder.SetField(descriptor.FindFieldByNumber(2)!, float.PositiveInfinity);
        builder.SetField(descriptor.FindFieldByNumber(3)!, float.NegativeInfinity);
        var message = builder.Build();

        var       serializer = new DynamicMessageJsonSerializer();
        using var doc        = JsonDocument.Parse(serializer.Write(message));

        await Assert.That(doc.RootElement.GetProperty("nanVal").GetString()).IsEqualTo("NaN");
        await Assert.That(doc.RootElement.GetProperty("infVal").GetString()).IsEqualTo("Infinity");
        await Assert.That(doc.RootElement.GetProperty("negInfVal").GetString()).IsEqualTo("-Infinity");
    }
}