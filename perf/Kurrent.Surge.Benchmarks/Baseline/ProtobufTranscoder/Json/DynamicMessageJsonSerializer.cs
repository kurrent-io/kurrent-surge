using System.Buffers;
using System.Text.Json;
using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Dynamic;

namespace ProtobufTranscoder.Json;

/// <summary>
/// Serializes DynamicMessage to proto canonical JSON.
/// Supports proto2, proto3, and editions presence semantics.
/// </summary>
public sealed class DynamicMessageJsonSerializer
{
    readonly JsonSerializeOptions _options;

    const string TimestampType = "google.protobuf.Timestamp";
    const string DurationType  = "google.protobuf.Duration";
    const string StructType    = "google.protobuf.Struct";
    const string ValueType     = "google.protobuf.Value";
    const string ListValueType = "google.protobuf.ListValue";
    const string FieldMaskType = "google.protobuf.FieldMask";
    const string EmptyType     = "google.protobuf.Empty";

    static readonly HashSet<string> WrapperTypes =
    [
        "google.protobuf.DoubleValue",
        "google.protobuf.FloatValue",
        "google.protobuf.Int64Value",
        "google.protobuf.UInt64Value",
        "google.protobuf.Int32Value",
        "google.protobuf.UInt32Value",
        "google.protobuf.BoolValue",
        "google.protobuf.StringValue",
        "google.protobuf.BytesValue",
    ];

    public DynamicMessageJsonSerializer(JsonSerializeOptions? options = null)
    {
        _options = options ?? new JsonSerializeOptions();
    }

    public void Write(DynamicMessage message, IBufferWriter<byte> output)
    {
        using var writer = new Utf8JsonWriter(output);
        WriteMessage(writer, message);
        writer.Flush();
    }

    public byte[] Write(DynamicMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Write(message, buffer);
        return buffer.WrittenSpan.ToArray();
    }

    internal ReadOnlyMemory<byte> WriteToMemory(DynamicMessage message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        Write(message, buffer);
        return buffer.WrittenMemory;
    }

    void WriteMessage(Utf8JsonWriter writer, DynamicMessage message)
    {
        var fullName = message.Descriptor.FullName;

        if (TryWriteWellKnownType(writer, message, fullName))
            return;

        writer.WriteStartObject();

        var hasExtensions = message.Descriptor.ExtensionRanges.Count > 0;
        HashSet<int>? emittedNumbers = hasExtensions ? new() : null;

        foreach (var field in message.Descriptor.Fields)
        {
            emittedNumbers?.Add(field.Number);

            if (!ShouldEmitField(message, field))
                continue;

            var value = message.GetField(field);
            if (value is null)
            {
                if (_options.EmitDefaults)
                {
                    WriteFieldName(writer, field);
                    WriteDefaultValue(writer, field);
                }
                continue;
            }

            WriteFieldName(writer, field);
            WriteFieldValue(writer, field, value);
        }

        if (hasExtensions)
        {
            foreach (var fieldNumber in message.AllFieldNumbers)
            {
                if (emittedNumbers!.Contains(fieldNumber)) continue;

                var field = message.Descriptor.FindFieldByNumber(fieldNumber);
                if (field is null) continue;

                var value = message.GetField(field);
                if (value is null) continue;

                WriteFieldName(writer, field);
                WriteFieldValue(writer, field, value);
            }
        }

        writer.WriteEndObject();
    }

    bool ShouldEmitField(DynamicMessage message, FieldDescriptor field)
    {
        if (field.ContainingOneof is not null)
            return message.HasField(field);

        if (field.IsRepeated)
            return message.HasField(field) || _options.EmitDefaults;

        switch (field.Presence)
        {
            case FieldPresence.Implicit:
                if (_options.EmitDefaults) return true;
                var value = message.GetField(field);
                return value is not null && !IsDefaultValue(value, field);

            case FieldPresence.Explicit:
            case FieldPresence.Required:
                return _options.EmitDefaults || message.HasField(field);

            default:
                return message.HasField(field);
        }
    }

    static bool IsDefaultValue(object value, FieldDescriptor field)
    {
        return value switch
        {
            int i => i == 0,
            long l => l == 0L,
            uint u => u == 0u,
            ulong ul => ul == 0uL,
            float f => f == 0.0f,
            double d => d == 0.0,
            bool b => !b,
            string s => s.Length == 0,
            byte[] bytes => bytes.Length == 0,
            EnumValueDescriptor evd => evd.Number == 0,
            _ => false,
        };
    }

    bool TryWriteWellKnownType(Utf8JsonWriter writer, DynamicMessage message, string fullName)
    {
        switch (fullName)
        {
            case TimestampType:
                WriteTimestamp(writer, message);
                return true;
            case DurationType:
                WriteDuration(writer, message);
                return true;
            case StructType:
                WriteStruct(writer, message);
                return true;
            case ValueType:
                WriteValue(writer, message);
                return true;
            case ListValueType:
                WriteListValue(writer, message);
                return true;
            case FieldMaskType:
                WriteFieldMask(writer, message);
                return true;
            case EmptyType:
                writer.WriteStartObject();
                writer.WriteEndObject();
                return true;
        }

        if (WrapperTypes.Contains(fullName))
        {
            WriteWrapper(writer, message);
            return true;
        }

        return false;
    }

    void WriteTimestamp(Utf8JsonWriter writer, DynamicMessage message)
    {
        long seconds = 0;
        var nanos = 0;

        var secondsField = message.Descriptor.FindFieldByNumber(1);
        var nanosField = message.Descriptor.FindFieldByNumber(2);

        if (secondsField is not null && message.GetField(secondsField) is long s) seconds = s;
        if (nanosField is not null && message.GetField(nanosField) is int n) nanos = n;

        var dt = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanos / 100);
        var formatted = FormatTimestamp(dt, nanos);
        writer.WriteStringValue(formatted);
    }

    static string FormatTimestamp(DateTimeOffset dt, int nanos)
    {
        if (nanos == 0)
            return dt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        if (nanos % 1_000_000 == 0)
            return dt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        if (nanos % 1_000 == 0)
            return dt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'");

        var nanoStr = dt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff").TrimEnd('0').TrimEnd('.');
        return nanoStr + "Z";
    }

    void WriteDuration(Utf8JsonWriter writer, DynamicMessage message)
    {
        long seconds = 0;
        var nanos = 0;

        var secondsField = message.Descriptor.FindFieldByNumber(1);
        var nanosField = message.Descriptor.FindFieldByNumber(2);

        if (secondsField is not null && message.GetField(secondsField) is long s) seconds = s;
        if (nanosField is not null && message.GetField(nanosField) is int n) nanos = n;

        var sign = "";
        if (seconds < 0 || nanos < 0)
        {
            sign = "-";
            seconds = Math.Abs(seconds);
            nanos = Math.Abs(nanos);
        }

        if (nanos == 0)
        {
            writer.WriteStringValue($"{sign}{seconds}s");
        }
        else
        {
            var frac = nanos.ToString("D9").TrimEnd('0');
            writer.WriteStringValue($"{sign}{seconds}.{frac}s");
        }
    }

    void WriteStruct(Utf8JsonWriter writer, DynamicMessage message)
    {
        writer.WriteStartObject();
        var fieldsField = message.Descriptor.FindFieldByNumber(1);
        if (fieldsField is not null && message.GetField(fieldsField) is List<object> entries)
        {
            foreach (var entry in entries)
            {
                if (entry is DynamicMessage entryMsg)
                {
                    var keyField = entryMsg.Descriptor.FindFieldByNumber(1);
                    var valueField = entryMsg.Descriptor.FindFieldByNumber(2);
                    if (keyField is not null && valueField is not null)
                    {
                        var key = entryMsg.GetField(keyField)?.ToString() ?? "";
                        writer.WritePropertyName(key);
                        var val = entryMsg.GetField(valueField);
                        if (val is DynamicMessage valMsg)
                            WriteValue(writer, valMsg);
                        else
                            writer.WriteNullValue();
                    }
                }
            }
        }
        writer.WriteEndObject();
    }

    void WriteValue(Utf8JsonWriter writer, DynamicMessage message)
    {
        var nullField = message.Descriptor.FindFieldByNumber(1);
        var numberField = message.Descriptor.FindFieldByNumber(2);
        var stringField = message.Descriptor.FindFieldByNumber(3);
        var boolField = message.Descriptor.FindFieldByNumber(4);
        var structField = message.Descriptor.FindFieldByNumber(5);
        var listField = message.Descriptor.FindFieldByNumber(6);

        if (numberField is not null && message.HasField(numberField))
        {
            var val = message.GetField(numberField);
            if (val is double d) writer.WriteNumberValue(d);
            else writer.WriteNumberValue(0);
            return;
        }

        if (stringField is not null && message.HasField(stringField))
        {
            writer.WriteStringValue(message.GetField(stringField) as string ?? "");
            return;
        }

        if (boolField is not null && message.HasField(boolField))
        {
            writer.WriteBooleanValue(message.GetField(boolField) is true);
            return;
        }

        if (structField is not null && message.HasField(structField) &&
            message.GetField(structField) is DynamicMessage structMsg)
        {
            WriteStruct(writer, structMsg);
            return;
        }

        if (listField is not null && message.HasField(listField) &&
            message.GetField(listField) is DynamicMessage listMsg)
        {
            WriteListValue(writer, listMsg);
            return;
        }

        writer.WriteNullValue();
    }

    void WriteListValue(Utf8JsonWriter writer, DynamicMessage message)
    {
        writer.WriteStartArray();
        var valuesField = message.Descriptor.FindFieldByNumber(1);
        if (valuesField is not null && message.GetField(valuesField) is List<object> values)
        {
            foreach (var item in values)
            {
                if (item is DynamicMessage valMsg)
                    WriteValue(writer, valMsg);
                else
                    writer.WriteNullValue();
            }
        }
        writer.WriteEndArray();
    }

    void WriteFieldMask(Utf8JsonWriter writer, DynamicMessage message)
    {
        var pathsField = message.Descriptor.FindFieldByNumber(1);
        if (pathsField is not null && message.GetField(pathsField) is List<object> paths)
        {
            var camelPaths = paths
                .Select(p => FieldDescriptor.ToJsonName(p?.ToString() ?? ""));
            writer.WriteStringValue(string.Join(",", camelPaths));
        }
        else
        {
            writer.WriteStringValue("");
        }
    }

    void WriteWrapper(Utf8JsonWriter writer, DynamicMessage message)
    {
        var valueField = message.Descriptor.FindFieldByNumber(1);
        if (valueField is null || !message.HasField(valueField))
        {
            writer.WriteNullValue();
            return;
        }

        var value = message.GetField(valueField)!;
        WriteScalarValue(writer, valueField, value);
    }

    void WriteFieldName(Utf8JsonWriter writer, FieldDescriptor field)
    {
        writer.WritePropertyName(_options.UseProtoFieldNames ? field.Name : field.JsonName);
    }

    void WriteFieldValue(Utf8JsonWriter writer, FieldDescriptor field, object value)
    {
        if (field.IsMap && field.MessageType is not null)
        {
            WriteMapField(writer, field, value);
            return;
        }

        if (field.IsRepeated)
        {
            writer.WriteStartArray();
            if (value is List<object> list)
            {
                foreach (var item in list)
                    WriteSingleValue(writer, field, item);
            }
            writer.WriteEndArray();
            return;
        }

        WriteSingleValue(writer, field, value);
    }

    void WriteMapField(Utf8JsonWriter writer, FieldDescriptor field, object value)
    {
        writer.WriteStartObject();
        if (value is List<object> entries)
        {
            foreach (var entry in entries)
            {
                if (entry is DynamicMessage entryMsg)
                {
                    var keyField = entryMsg.Descriptor.FindFieldByNumber(1);
                    var valueField = entryMsg.Descriptor.FindFieldByNumber(2);
                    if (keyField is not null && valueField is not null)
                    {
                        var key = entryMsg.GetField(keyField)?.ToString() ?? "";
                        writer.WritePropertyName(key);

                        var val = entryMsg.GetField(valueField);
                        if (val is not null)
                            WriteSingleValue(writer, valueField, val);
                        else
                            WriteDefaultValue(writer, valueField);
                    }
                }
            }
        }
        writer.WriteEndObject();
    }

    void WriteSingleValue(Utf8JsonWriter writer, FieldDescriptor field, object value)
    {
        if ((field.Type is FieldType.Message or FieldType.Group) && value is DynamicMessage msg)
        {
            WriteMessage(writer, msg);
            return;
        }

        if (field.Type == FieldType.Enum)
        {
            WriteEnumValue(writer, field, value);
            return;
        }

        WriteScalarValue(writer, field, value);
    }

    void WriteEnumValue(Utf8JsonWriter writer, FieldDescriptor field, object value)
    {
        if (value is EnumValueDescriptor evd)
        {
            if (_options.EnumsAsIntegers)
                writer.WriteNumberValue(evd.Number);
            else
                writer.WriteStringValue(evd.Name);
        }
        else if (value is int i)
        {
            // Unknown enum value (not in descriptor) — always emit as integer
            writer.WriteNumberValue(i);
        }
        else
        {
            writer.WriteStringValue(value.ToString());
        }
    }

    static void WriteScalarValue(Utf8JsonWriter writer, FieldDescriptor field, object value)
    {
        switch (field.Type)
        {
            case FieldType.Int32 or FieldType.SInt32 or FieldType.SFixed32:
                writer.WriteNumberValue(Convert.ToInt32(value));
                break;

            case FieldType.UInt32 or FieldType.Fixed32:
                writer.WriteNumberValue(Convert.ToUInt32(value));
                break;

            // 64-bit integers are serialized as strings per proto JSON spec
            case FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64:
                writer.WriteStringValue(Convert.ToInt64(value).ToString());
                break;

            case FieldType.UInt64 or FieldType.Fixed64:
                writer.WriteStringValue(Convert.ToUInt64(value).ToString());
                break;

            case FieldType.Float:
            {
                var f = Convert.ToSingle(value);
                if (float.IsNaN(f)) writer.WriteStringValue("NaN");
                else if (float.IsPositiveInfinity(f)) writer.WriteStringValue("Infinity");
                else if (float.IsNegativeInfinity(f)) writer.WriteStringValue("-Infinity");
                else writer.WriteNumberValue(f);
                break;
            }

            case FieldType.Double:
            {
                var d = Convert.ToDouble(value);
                if (double.IsNaN(d)) writer.WriteStringValue("NaN");
                else if (double.IsPositiveInfinity(d)) writer.WriteStringValue("Infinity");
                else if (double.IsNegativeInfinity(d)) writer.WriteStringValue("-Infinity");
                else writer.WriteNumberValue(d);
                break;
            }

            case FieldType.Bool:
                writer.WriteBooleanValue(value is true);
                break;

            case FieldType.String:
                writer.WriteStringValue(value as string ?? value.ToString());
                break;

            case FieldType.Bytes:
                if (value is byte[] bytes)
                    writer.WriteStringValue(Convert.ToBase64String(bytes));
                else
                    writer.WriteStringValue("");
                break;

            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    static void WriteDefaultValue(Utf8JsonWriter writer, FieldDescriptor field)
    {
        if (field.IsRepeated)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        switch (field.Type)
        {
            case FieldType.Bool:
                writer.WriteBooleanValue(false);
                break;
            case FieldType.String:
            case FieldType.Bytes:
                writer.WriteStringValue("");
                break;
            case FieldType.Enum:
                writer.WriteNumberValue(0);
                break;
            case FieldType.Message or FieldType.Group:
                writer.WriteNullValue();
                break;
            case FieldType.Int64 or FieldType.SInt64 or FieldType.SFixed64 or
                 FieldType.UInt64 or FieldType.Fixed64:
                writer.WriteStringValue("0");
                break;
            default:
                writer.WriteNumberValue(0);
                break;
        }
    }
}
