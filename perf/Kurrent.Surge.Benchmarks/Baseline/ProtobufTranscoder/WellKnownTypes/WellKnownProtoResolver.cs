namespace ProtobufTranscoder.WellKnownTypes;

/// <summary>
/// Resolves well-known .proto files embedded in the library.
/// </summary>
sealed class WellKnownProtoResolver : IProtoSchemaResolver
{
    static readonly Dictionary<string, string> Protos = new(StringComparer.Ordinal)
    {
        ["google/protobuf/timestamp.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message Timestamp {
              int64 seconds = 1;
              int32 nanos = 2;
            }
            """,

        ["google/protobuf/duration.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message Duration {
              int64 seconds = 1;
              int32 nanos = 2;
            }
            """,

        ["google/protobuf/wrappers.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message DoubleValue { double value = 1; }
            message FloatValue { float value = 1; }
            message Int64Value { int64 value = 1; }
            message UInt64Value { uint64 value = 1; }
            message Int32Value { int32 value = 1; }
            message UInt32Value { uint32 value = 1; }
            message BoolValue { bool value = 1; }
            message StringValue { string value = 1; }
            message BytesValue { bytes value = 1; }
            """,

        ["google/protobuf/struct.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message Struct {
              map<string, Value> fields = 1;
            }
            message Value {
              oneof kind {
                NullValue null_value = 1;
                double number_value = 2;
                string string_value = 3;
                bool bool_value = 4;
                Struct struct_value = 5;
                ListValue list_value = 6;
              }
            }
            message ListValue {
              repeated Value values = 1;
            }
            enum NullValue {
              NULL_VALUE = 0;
            }
            """,

        ["google/protobuf/any.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message Any {
              string type_url = 1;
              bytes value = 2;
            }
            """,

        ["google/protobuf/empty.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message Empty {}
            """,

        ["google/protobuf/field_mask.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            option csharp_namespace = "Google.Protobuf.WellKnownTypes";
            message FieldMask {
              repeated string paths = 1;
            }
            """,

        ["google/protobuf/descriptor.proto"] = """
            syntax = "proto3";
            package google.protobuf;
            // Minimal stub — the full descriptor.proto is enormous.
            // Only included to satisfy imports.
            """,
    };

    public string? Resolve(string importPath) =>
        Protos.GetValueOrDefault(importPath);
}
