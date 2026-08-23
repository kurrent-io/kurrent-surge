using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Wire;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Json;

/// <summary>
/// The parts of the canonical protobuf JSON mapping that do not depend on how a message is walked.
/// </summary>
/// <remarks>
/// Shared by every writer strategy on purpose: the strategies differ in traversal, never in what a
/// value looks like once read. See https://protobuf.dev/programming-guides/json/.
/// </remarks>
static class ProtoJson {
    public const string AnyTypeName = "google.protobuf.Any";

    const string TimestampTypeName = "google.protobuf.Timestamp";
    const string DurationTypeName  = "google.protobuf.Duration";
    const string StructTypeName    = "google.protobuf.Struct";
    const string ValueTypeName     = "google.protobuf.Value";
    const string ListValueTypeName = "google.protobuf.ListValue";
    const string FieldMaskTypeName = "google.protobuf.FieldMask";
    const string EmptyTypeName     = "google.protobuf.Empty";

    /// <summary>True when the descriptor has a JSON form of its own rather than an object of fields.</summary>
    public static bool IsWellKnown(MessageDescriptor descriptor) =>
        descriptor.File.Package == "google.protobuf"
     && descriptor.FullName is TimestampTypeName or DurationTypeName or StructTypeName or ValueTypeName
                            or ListValueTypeName or FieldMaskTypeName or EmptyTypeName or AnyTypeName
     || IsWrapper(descriptor);

    static bool IsWrapper(MessageDescriptor descriptor) =>
        descriptor.File.Name == "google/protobuf/wrappers.proto";

    /// <summary>
    /// Writes a well-known type's JSON form.
    /// </summary>
    /// <remarks>
    /// <c>Any</c> is excluded: its payload is a message of a type named at run time, so it has to be
    /// written by whichever strategy owns the traversal.
    /// </remarks>
    public static bool TryWriteWellKnown(
        MessageDescriptor descriptor, ReadOnlySpan<byte> data, Utf8JsonWriter writer, ProtobufJsonWriterOptions options
    ) {
        if (IsWrapper(descriptor)) {
            WriteWrapper(descriptor, data, writer);
            return true;
        }

        switch (descriptor.FullName) {
            case TimestampTypeName:
                WriteTimestamp(data, writer);
                return true;

            case DurationTypeName:
                WriteDuration(data, writer);
                return true;

            case StructTypeName:
                WriteStruct(descriptor, data, writer, options);
                return true;

            case ValueTypeName:
                WriteValue(descriptor, data, writer, options);
                return true;

            case ListValueTypeName:
                WriteListValue(descriptor, data, writer, options);
                return true;

            case FieldMaskTypeName:
                WriteFieldMask(data, writer);
                return true;

            case EmptyTypeName:
                writer.WriteStartObject();
                writer.WriteEndObject();
                return true;

            default:
                return false;
        }
    }

    /// <remarks>
    /// The name is pre-escaped and pre-encoded once per field. <see cref="Utf8JsonWriter"/> scans a
    /// <see cref="string"/> property name for characters needing escapes on every call and then
    /// transcodes it; <see cref="JsonEncodedText"/> has already done both, so the write is a copy.
    /// A message pays this once per declared field, on every message of that type in the payload.
    /// </remarks>
    public static void WriteFieldName(Utf8JsonWriter writer, FieldDescriptor field, ProtobufJsonWriterOptions options) =>
        writer.WritePropertyName(NamesOf(field.ContainingType)[field.Index].Select(options.UseProtoFieldNames));

    /// <summary>A field's two spellings, encoded once.</summary>
    readonly record struct FieldNames(JsonEncodedText Json, JsonEncodedText Proto) {
        public JsonEncodedText Select(bool useProtoName) => useProtoName ? Proto : Json;
    }

    /// <remarks>
    /// Keyed by message rather than by field, so a message costs one lookup and then an array index
    /// per field. Descriptors live as long as the schema set that built them, so the cache is
    /// bounded by the schemas in use.
    /// </remarks>
    static readonly ConcurrentDictionary<MessageDescriptor, FieldNames[]> NameCache = new();

    static FieldNames[] NamesOf(MessageDescriptor descriptor) =>
        NameCache.GetOrAdd(descriptor, static message => {
            var names = new FieldNames[message.Fields.InDeclarationOrder().Count];

            foreach (var field in message.Fields.InDeclarationOrder())
                names[field.Index] = new FieldNames(JsonEncodedText.Encode(field.JsonName), JsonEncodedText.Encode(field.Name));

            return names;
        });

    /// <summary>Reads one value of <paramref name="field"/> and writes its JSON form.</summary>
    /// <remarks>Message and group fields are not handled here — the strategy owns recursion.</remarks>
    public static void WriteScalar(
        ref ProtoWireReader reader, FieldDescriptor field, Utf8JsonWriter writer, ProtobufJsonWriterOptions options
    ) {
        switch (field.FieldType) {
            case Type.Bool:
                writer.WriteBooleanValue(reader.ReadVarint() != 0);
                break;

            case Type.Int32:
                writer.WriteNumberValue((int)(long)reader.ReadVarint());
                break;

            case Type.UInt32:
                writer.WriteNumberValue((uint)reader.ReadVarint());
                break;

            case Type.SInt32:
                writer.WriteNumberValue(ProtoWireReader.DecodeZigZag32((uint)reader.ReadVarint()));
                break;

            case Type.Fixed32:
                writer.WriteNumberValue(reader.ReadFixed32());
                break;

            case Type.SFixed32:
                writer.WriteNumberValue((int)reader.ReadFixed32());
                break;

            // 64-bit integers are strings in the canonical mapping, because a JSON number cannot
            // hold the full range without loss.
            case Type.Int64:
                WriteInt64Value(writer, (long)reader.ReadVarint());
                break;

            case Type.UInt64:
                WriteUInt64Value(writer, reader.ReadVarint());
                break;

            case Type.SInt64:
                WriteInt64Value(writer, ProtoWireReader.DecodeZigZag64(reader.ReadVarint()));
                break;

            case Type.Fixed64:
                WriteUInt64Value(writer, reader.ReadFixed64());
                break;

            case Type.SFixed64:
                WriteInt64Value(writer, (long)reader.ReadFixed64());
                break;

            case Type.Float:
                WriteFloat(BitConverter.UInt32BitsToSingle(reader.ReadFixed32()), writer);
                break;

            case Type.Double:
                WriteDouble(BitConverter.UInt64BitsToDouble(reader.ReadFixed64()), writer);
                break;

            // Written from the wire bytes rather than through a string: the payload is already
            // UTF-8, and the writer escapes UTF-8 directly. Decoding to UTF-16 first would allocate
            // a string per occurrence and then transcode it straight back.
            case Type.String:
                writer.WriteStringValue(reader.ReadLengthDelimited());
                break;

            case Type.Bytes:
                WriteBase64(reader.ReadLengthDelimited(), writer);
                break;

            case Type.Enum:
                WriteEnum((int)(long)reader.ReadVarint(), field, writer, options);
                break;

            default:
                throw new InvalidDataException($"Field '{field.FullName}' has type {field.FieldType}, which has no scalar form.");
        }
    }

    /// <summary>Writes the value a field carries when it is absent from the payload.</summary>
    public static void WriteDefault(Utf8JsonWriter writer, FieldDescriptor field) {
        if (field.IsRepeated) {
            if (field.IsMap) {
                writer.WriteStartObject();
                writer.WriteEndObject();
            }
            else {
                writer.WriteStartArray();
                writer.WriteEndArray();
            }

            return;
        }

        switch (field.FieldType) {
            case Type.Bool:
                writer.WriteBooleanValue(false);
                break;

            case Type.String:
            case Type.Bytes:
                writer.WriteStringValue("");
                break;

            case Type.Int64 or Type.UInt64 or Type.SInt64 or Type.Fixed64 or Type.SFixed64:
                writer.WriteStringValue("0");
                break;

            case Type.Message or Type.Group:
                writer.WriteNullValue();
                break;

            case Type.Enum:
                WriteEnumDefault(field, writer);
                break;

            default:
                writer.WriteNumberValue(0);
                break;
        }
    }

    public static bool IsPackable(Type type) =>
        type is not (Type.String or Type.Bytes or Type.Message or Type.Group);

    #region ->> Number formatting <<-

    /// <summary>Widest decimal form of a 64-bit integer: <c>-9223372036854775808</c>.</summary>
    public const int Int64BufferSize = 20;

    /// <summary>Formats <paramref name="value"/> as UTF-8 decimal digits, returning the count written.</summary>
    /// <remarks>
    /// The canonical mapping spells 64-bit integers as JSON strings, and both a value and a map key
    /// need them. Formatting straight to UTF-8 skips the intermediate <see cref="string"/> and the
    /// UTF-16 to UTF-8 transcode the writer would otherwise perform.
    /// </remarks>
    public static int Format(Span<byte> destination, long value) {
        value.TryFormat(destination, out var written, provider: CultureInfo.InvariantCulture);
        return written;
    }

    /// <inheritdoc cref="Format(Span{byte}, long)"/>
    public static int Format(Span<byte> destination, ulong value) {
        value.TryFormat(destination, out var written, provider: CultureInfo.InvariantCulture);
        return written;
    }

    static void WriteInt64Value(Utf8JsonWriter writer, long value) {
        Span<byte> buffer = stackalloc byte[Int64BufferSize];
        writer.WriteStringValue(buffer[..Format(buffer, value)]);
    }

    static void WriteUInt64Value(Utf8JsonWriter writer, ulong value) {
        Span<byte> buffer = stackalloc byte[Int64BufferSize];
        writer.WriteStringValue(buffer[..Format(buffer, value)]);
    }

    #endregion // Number formatting

    #region ->> Scalars <<-

    static void WriteEnum(int number, FieldDescriptor field, Utf8JsonWriter writer, ProtobufJsonWriterOptions options) {
        if (options.EnumsAsIntegers) {
            writer.WriteNumberValue(number);
            return;
        }

        WriteEnumName(field.EnumType, number, writer);
    }

    static void WriteEnumDefault(FieldDescriptor field, Utf8JsonWriter writer) => WriteEnumName(field.EnumType, 0, writer);

    /// <remarks>
    /// An open enum accepts a number the schema has never heard of. The canonical mapping emits it
    /// as a number, because there is no name to emit.
    /// </remarks>
    static void WriteEnumName(EnumDescriptor descriptor, int number, Utf8JsonWriter writer) {
        if (NamesOf(descriptor).TryGetValue(number, out var name)) writer.WriteStringValue(name);
        else writer.WriteNumberValue(number);
    }

    /// <remarks>
    /// Encoded once per enum, for the same reason field names are: the writer would otherwise scan
    /// and transcode the name on every value it emits.
    /// </remarks>
    static readonly ConcurrentDictionary<EnumDescriptor, FrozenDictionary<int, JsonEncodedText>> EnumNameCache = new();

    static FrozenDictionary<int, JsonEncodedText> NamesOf(EnumDescriptor descriptor) =>
        EnumNameCache.GetOrAdd(descriptor, static declared => declared.Values
            // An alias lets two names share a number. The first declared one wins, which is what
            // every implementation emits.
            .DistinctBy(value => value.Number)
            .ToFrozenDictionary(value => value.Number, value => JsonEncodedText.Encode(value.Name)));

    // The three non-finite values have no JSON number form, so the mapping spells them as strings.
    static void WriteFloat(float value, Utf8JsonWriter writer) {
        if (float.IsNaN(value)) writer.WriteStringValue("NaN");
        else if (float.IsPositiveInfinity(value)) writer.WriteStringValue("Infinity");
        else if (float.IsNegativeInfinity(value)) writer.WriteStringValue("-Infinity");
        else writer.WriteNumberValue(value);
    }

    static void WriteDouble(double value, Utf8JsonWriter writer) {
        if (double.IsNaN(value)) writer.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(value)) writer.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(value)) writer.WriteStringValue("-Infinity");
        else writer.WriteNumberValue(value);
    }

    static void WriteBase64(ReadOnlySpan<byte> data, Utf8JsonWriter writer) {
        if (data.IsEmpty) {
            writer.WriteStringValue("");
            return;
        }

        var length = Base64.GetMaxEncodedToUtf8Length(data.Length);

        byte[]? rented = null;

        var buffer = length <= 256 ? stackalloc byte[256] : rented = ArrayPool<byte>.Shared.Rent(length);

        try {
            Base64.EncodeToUtf8(data, buffer, out _, out var written);

            writer.WriteStringValue(buffer[..written]);
        }
        finally {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    #endregion // Scalars

    #region ->> Well-known types <<-

    /// <summary>Reads one numeric field of a fixed-shape message, without building anything.</summary>
    /// <remarks>
    /// Only <c>Timestamp</c> and <c>Duration</c> need this, and both declare their two fields as
    /// <c>int64</c> and <c>int32</c> — always varint on the wire. A length-delimited value at those
    /// numbers is a payload that does not match the schema, so it is skipped rather than decoded.
    /// </remarks>
    static bool TryFindNumericField(ReadOnlySpan<byte> data, int fieldNumber, out ulong scalar) {
        var reader = new ProtoWireReader(data);

        scalar = 0;

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number != fieldNumber) {
                reader.Skip(kind, number);
                continue;
            }

            switch (kind) {
                case ProtoWireType.Varint:
                    scalar = reader.ReadVarint();
                    return true;

                case ProtoWireType.Fixed32:
                    scalar = reader.ReadFixed32();
                    return true;

                case ProtoWireType.Fixed64:
                    scalar = reader.ReadFixed64();
                    return true;

                default:
                    reader.Skip(kind, number);
                    continue;
            }
        }

        return false;
    }

    static (long Seconds, int Nanos) ReadSecondsAndNanos(ReadOnlySpan<byte> data) {
        var seconds = TryFindNumericField(data, 1, out var rawSeconds) ? (long)rawSeconds : 0L;
        var nanos   = TryFindNumericField(data, 2, out var rawNanos) ? (int)(long)rawNanos : 0;

        return (seconds, nanos);
    }

    /// <summary>Longest temporal form: 20 sign-and-seconds bytes, 10 fractional, one suffix.</summary>
    const int TemporalBufferSize = 32;

    // RFC 3339 with 0, 3, 6 or 9 fractional digits — never another count.
    // https://protobuf.dev/programming-guides/json/
    static void WriteTimestamp(ReadOnlySpan<byte> data, Utf8JsonWriter writer) {
        var (seconds, nanos) = ReadSecondsAndNanos(data);

        var instant = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        Span<byte> buffer = stackalloc byte[TemporalBufferSize];

        instant.TryFormat(buffer, out var written, "yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);

        written += FormatNanos(buffer[written..], nanos);

        buffer[written++] = (byte)'Z';

        writer.WriteStringValue(buffer[..written]);
    }

    static void WriteDuration(ReadOnlySpan<byte> data, Utf8JsonWriter writer) {
        var (seconds, nanos) = ReadSecondsAndNanos(data);

        var negative = seconds < 0 || nanos < 0;

        // The magnitude is taken unsigned: Math.Abs(long.MinValue) overflows, and a payload is free
        // to carry that value even though the format's own range stops far short of it.
        var magnitude = negative ? (seconds < 0 ? (ulong)-(seconds + 1) + 1 : (ulong)seconds) : (ulong)seconds;

        if (negative) nanos = Math.Abs(nanos);

        Span<byte> buffer = stackalloc byte[TemporalBufferSize];

        var written = 0;

        if (negative) buffer[written++] = (byte)'-';

        written += Format(buffer[written..], magnitude);
        written += FormatNanos(buffer[written..], nanos);

        buffer[written++] = (byte)'s';

        writer.WriteStringValue(buffer[..written]);
    }

    /// <remarks>
    /// The fraction is emitted at 3, 6 or 9 digits and never another count, so a reader can tell
    /// milliseconds from microseconds from nanoseconds without counting significant figures.
    /// </remarks>
    static int FormatNanos(Span<byte> destination, int nanos) {
        if (nanos == 0) return 0;

        destination[0] = (byte)'.';

        var (value, format) = nanos % 1_000_000 == 0 ? (nanos / 1_000_000, "D3")
                            : nanos % 1_000 == 0     ? (nanos / 1_000, "D6")
                            :                          (nanos, "D9");

        value.TryFormat(destination[1..], out var written, format, CultureInfo.InvariantCulture);

        return written + 1;
    }

    static void WriteFieldMask(ReadOnlySpan<byte> data, Utf8JsonWriter writer) {
        var reader = new ProtoWireReader(data);

        // The paths are joined into one string value, so they are built end to end in a single
        // buffer rather than as a list of strings that is then joined.
        var buffer = new ArrayBufferWriter<byte>(64);
        var first  = true;

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number != 1 || kind != ProtoWireType.LengthDelimited) {
                reader.Skip(kind, number);
                continue;
            }

            // Tracked separately from the byte count: a path is allowed to be empty, and counting
            // bytes would then drop the separator that keeps two of them apart.
            if (!first) {
                buffer.GetSpan(1)[0] = (byte)',';
                buffer.Advance(1);
            }

            WriteJsonName(buffer, reader.ReadLengthDelimited());

            first = false;
        }

        writer.WriteStringValue(buffer.WrittenSpan);
    }

    static void WriteWrapper(MessageDescriptor descriptor, ReadOnlySpan<byte> data, Utf8JsonWriter writer) {
        var field  = descriptor.FindFieldByNumber(1);
        var reader = new ProtoWireReader(data);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number != 1) {
                reader.Skip(kind, number);
                continue;
            }

            WriteScalar(ref reader, field, writer, ProtobufJsonWriterOptions.Default);

            return;
        }

        // An absent value still has to produce the wrapped type's zero, because a wrapper written
        // as an empty message means "present and default", not "absent".
        WriteDefault(writer, field);
    }

    static void WriteStruct(MessageDescriptor descriptor, ReadOnlySpan<byte> data, Utf8JsonWriter writer, ProtobufJsonWriterOptions options) {
        var entry = descriptor.FindFieldByNumber(1).MessageType;
        var value = entry.FindFieldByNumber(2).MessageType;

        writer.WriteStartObject();

        var reader = new ProtoWireReader(data);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number != 1 || kind != ProtoWireType.LengthDelimited) {
                reader.Skip(kind, number);
                continue;
            }

            WriteStructEntry(reader.ReadLengthDelimited(), value, writer, options);
        }

        writer.WriteEndObject();
    }

    static void WriteStructEntry(ReadOnlySpan<byte> entry, MessageDescriptor valueDescriptor, Utf8JsonWriter writer, ProtobufJsonWriterOptions options) {
        var hasValue = false;

        ReadOnlySpan<byte> key   = default;
        ReadOnlySpan<byte> value = default;

        var reader = new ProtoWireReader(entry);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number == 1 && kind == ProtoWireType.LengthDelimited) {
                key = reader.ReadLengthDelimited();
            }
            else if (number == 2 && kind == ProtoWireType.LengthDelimited) {
                value    = reader.ReadLengthDelimited();
                hasValue = true;
            }
            else {
                reader.Skip(kind, number);
            }
        }

        writer.WritePropertyName(key);

        if (hasValue) WriteValue(valueDescriptor, value, writer, options);
        else writer.WriteNullValue();
    }

    /// <remarks>
    /// <c>Value</c> is a oneof of six branches. Whichever is present on the wire is the one to
    /// write, and an empty payload is a JSON null.
    /// </remarks>
    static void WriteValue(MessageDescriptor descriptor, ReadOnlySpan<byte> data, Utf8JsonWriter writer, ProtobufJsonWriterOptions options) {
        var reader = new ProtoWireReader(data);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            switch (number) {
                case 1:
                    reader.ReadVarint();
                    writer.WriteNullValue();
                    return;

                case 2:
                    WriteDouble(BitConverter.UInt64BitsToDouble(reader.ReadFixed64()), writer);
                    return;

                case 3:
                    writer.WriteStringValue(reader.ReadLengthDelimited());
                    return;

                case 4:
                    writer.WriteBooleanValue(reader.ReadVarint() != 0);
                    return;

                case 5:
                    WriteStruct(descriptor.FindFieldByNumber(5).MessageType, reader.ReadLengthDelimited(), writer, options);
                    return;

                case 6:
                    WriteListValue(descriptor.FindFieldByNumber(6).MessageType, reader.ReadLengthDelimited(), writer, options);
                    return;

                default:
                    reader.Skip(kind, number);
                    break;
            }
        }

        writer.WriteNullValue();
    }

    static void WriteListValue(MessageDescriptor descriptor, ReadOnlySpan<byte> data, Utf8JsonWriter writer, ProtobufJsonWriterOptions options) {
        var value = descriptor.FindFieldByNumber(1).MessageType;

        writer.WriteStartArray();

        var reader = new ProtoWireReader(data);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number = ProtoWireReader.FieldNumberOf(tag);
            var kind   = ProtoWireReader.WireTypeOf(tag);

            if (number == 1 && kind == ProtoWireType.LengthDelimited) WriteValue(value, reader.ReadLengthDelimited(), writer, options);
            else reader.Skip(kind, number);
        }

        writer.WriteEndArray();
    }

    /// <remarks>
    /// A field mask travels as <c>snake_case</c> paths and is emitted as lower camel case, one
    /// segment at a time. The path is written straight through from its wire bytes: it is never
    /// longer than its source, and a path is ASCII by the language's own identifier rules, so a
    /// byte of input maps to at most a byte of output.
    /// <para>
    /// Sized from the destination rather than from the input on purpose. A path arrives off the
    /// wire with no length ceiling, so a stack buffer cut to fit it is a stack overflow waiting for
    /// the right payload.
    /// </para>
    /// </remarks>
    static void WriteJsonName(ArrayBufferWriter<byte> destination, ReadOnlySpan<byte> path) {
        var buffer = destination.GetSpan(path.Length);

        var length     = 0;
        var capitalize = false;

        foreach (var c in path) {
            if (c == (byte)'_') {
                capitalize = true;
                continue;
            }

            buffer[length++] = capitalize && c is >= (byte)'a' and <= (byte)'z' ? (byte)(c - 32) : c;
            capitalize       = false;
        }

        destination.Advance(length);
    }

    #endregion // Well-known types
}
