using ProtobufTranscoder.Descriptors;
using ProtobufTranscoder.Wire;

namespace ProtobufTranscoder.Dynamic;

/// <summary>Immutable decoded protobuf message.</summary>
public sealed class DynamicMessage
{
    readonly Dictionary<int, object> _fields;
    readonly HashSet<int>            _presentFields;

    public MessageDescriptor Descriptor { get; }

    internal IEnumerable<int> AllFieldNumbers => _fields.Keys;

    DynamicMessage(MessageDescriptor descriptor, Dictionary<int, object> fields, HashSet<int> presentFields)
    {
        Descriptor = descriptor;
        _fields = fields;
        _presentFields = presentFields;
    }

    public object? GetField(FieldDescriptor field)
    {
        if (_fields.TryGetValue(field.Number, out var value))
            return value;

        if (!field.IsRepeated && field.DefaultValue is not null)
            return field.DefaultValue;

        return null;
    }

    public bool HasField(FieldDescriptor field) =>
        _presentFields.Contains(field.Number);

    public bool HasOneof(OneofDescriptor oneof)
    {
        foreach (var field in oneof.Fields)
        {
            if (_presentFields.Contains(field.Number))
                return true;
        }
        return false;
    }

    public FieldDescriptor? GetOneofFieldDescriptor(OneofDescriptor oneof)
    {
        foreach (var field in oneof.Fields)
        {
            if (_presentFields.Contains(field.Number))
                return field;
        }
        return null;
    }

    public static DynamicMessage Decode(MessageDescriptor descriptor, ReadOnlySpan<byte> protobufBytes)
    {
        var reader = new CodedReader(protobufBytes);
        return Decode(descriptor, ref reader);
    }

    internal static DynamicMessage Decode(MessageDescriptor descriptor, ref CodedReader reader)
    {
        var builder = Builder.NewBuilder(descriptor);

        while (!reader.IsAtEnd)
        {
            var tag = reader.ReadTag();
            if (tag == 0) break;

            var fieldNumber = CodedReader.GetFieldNumber(tag);
            var wireType = CodedReader.GetWireType(tag);
            var field = descriptor.FindFieldByNumber(fieldNumber);

            if (field is null)
            {
                reader.SkipField(tag);
                continue;
            }

            if (field.IsPacked && wireType == WireType.LengthDelimited && IsPackableType(field.Type))
            {
                var subReader = reader.ReadMessage();
                while (!subReader.IsAtEnd)
                {
                    var value = ReadValue(ref subReader, field);
                    builder.AddRepeatedField(field, value);
                }
            }
            else
            {
                var value = ReadValue(ref reader, field);
                if (field.IsRepeated)
                    builder.AddRepeatedField(field, value);
                else
                    builder.SetField(field, value);
            }
        }

        return builder.Build();
    }

    static object ReadValue(ref CodedReader reader, FieldDescriptor field)
    {
        return field.Type switch
        {
            FieldType.Int32 => reader.ReadInt32(),
            FieldType.Int64 => reader.ReadInt64(),
            FieldType.UInt32 => reader.ReadUInt32(),
            FieldType.UInt64 => reader.ReadUInt64(),
            FieldType.SInt32 => reader.ReadSInt32(),
            FieldType.SInt64 => reader.ReadSInt64(),
            FieldType.Bool => reader.ReadBool(),
            FieldType.Float => reader.ReadFloat(),
            FieldType.Double => reader.ReadDouble(),
            FieldType.Fixed32 => reader.ReadFixed32(),
            FieldType.Fixed64 => reader.ReadFixed64(),
            FieldType.SFixed32 => reader.ReadSFixed32(),
            FieldType.SFixed64 => reader.ReadSFixed64(),
            FieldType.String => reader.ReadString(),
            FieldType.Bytes => reader.ReadBytes().ToArray(),
            FieldType.Enum => ReadEnumValue(ref reader, field),
            FieldType.Message when field.IsGroupEncoded => DecodeGroupMessage(ref reader, field),
            FieldType.Message => DecodeSubMessage(ref reader, field),
            FieldType.Group => DecodeGroupMessage(ref reader, field),
            _ => throw new DecodeException($"Unknown field type: {field.Type}"),
        };
    }

    static object ReadEnumValue(ref CodedReader reader, FieldDescriptor field)
    {
        var number = reader.ReadEnum();
        if (field.EnumType is not null)
            return field.EnumType.FindValueByNumber(number) ?? (object)number;
        return number;
    }

    static DynamicMessage DecodeSubMessage(ref CodedReader reader, FieldDescriptor field)
    {
        if (field.MessageType is null)
            throw new DecodeException($"Field '{field.Name}' has message type but no MessageDescriptor linked.");

        var subReader = reader.ReadMessage();
        return Decode(field.MessageType, ref subReader);
    }

    static DynamicMessage DecodeGroupMessage(ref CodedReader reader, FieldDescriptor field)
    {
        if (field.MessageType is null)
            throw new DecodeException($"Field '{field.Name}' has group type but no MessageDescriptor linked.");

        var subReader = reader.ReadGroup(field.Number);
        return Decode(field.MessageType, ref subReader);
    }

    static bool IsPackableType(FieldType type) =>
        type is not (FieldType.String or FieldType.Bytes or FieldType.Message or FieldType.Group);

    public sealed class Builder
    {
        readonly MessageDescriptor       _descriptor;
        readonly Dictionary<int, object> _fields        = new();
        readonly HashSet<int>            _presentFields = new();

        Builder(MessageDescriptor descriptor) => _descriptor = descriptor;

        public static Builder NewBuilder(MessageDescriptor type) => new(type);

        public Builder SetField(FieldDescriptor field, object value)
        {
            _fields[field.Number] = value;
            _presentFields.Add(field.Number);
            return this;
        }

        public Builder AddRepeatedField(FieldDescriptor field, object value)
        {
            _presentFields.Add(field.Number);

            if (!_fields.TryGetValue(field.Number, out var existing))
            {
                var list = new List<object> { value };
                _fields[field.Number] = list;
            }
            else if (existing is List<object> list)
            {
                list.Add(value);
            }
            else
            {
                _fields[field.Number] = new List<object> { existing, value };
            }
            return this;
        }

        public Builder ClearField(FieldDescriptor field)
        {
            _fields.Remove(field.Number);
            _presentFields.Remove(field.Number);
            return this;
        }

        public DynamicMessage Build() => new(_descriptor, _fields, _presentFields);
    }
}
