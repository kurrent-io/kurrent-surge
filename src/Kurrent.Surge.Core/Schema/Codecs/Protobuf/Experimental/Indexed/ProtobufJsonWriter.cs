using System.Buffers;
using System.Text;
using System.Text.Json;
using Google.Protobuf.Reflection;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Json;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Schema;
using Kurrent.Surge.Codecs.Protobuf.Experimental.Wire;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Kurrent.Surge.Codecs.Protobuf.Experimental.Indexed;

/// <summary>
/// Rewrites a protobuf payload as JSON, guided by the schema that wrote it.
/// </summary>
/// <remarks>
/// Two passes per message. The first records where each field's bytes start; the second walks the
/// schema in field-number order and decodes from those offsets. Nothing is materialised, so a
/// scalar is never boxed and a repeated field never builds a list.
/// <para>
/// The wire format forces the two passes: fields arrive in any order, a repeated field's elements
/// can be split across non-adjacent runs, and a repeated singular field takes its last value. None
/// of that can be emitted correctly while still reading.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class ProtobufJsonWriter {
    readonly ProtoSchemaSet            _schema;
    readonly MessageDescriptor         _descriptor;
    readonly ProtobufJsonWriterOptions _options;

    /// <param name="schema">The schema set the message is declared in.</param>
    /// <param name="messageTypeName">The fully qualified message name, such as <c>orders.v1.Order</c>.</param>
    /// <param name="options">Emission options. Defaults to the canonical mapping.</param>
    /// <exception cref="ArgumentException">The message is not declared in the schema set.</exception>
    public ProtobufJsonWriter(ProtoSchemaSet schema, string messageTypeName, ProtobufJsonWriterOptions? options = null) {
        _schema     = schema;
        _descriptor = schema.FindMessage(messageTypeName);
        _options    = options ?? ProtobufJsonWriterOptions.Default;
    }

    /// <param name="protoSchema">A single <c>.proto</c> source, as text.</param>
    /// <param name="messageTypeName">The fully qualified message name.</param>
    /// <param name="options">Emission options. Defaults to the canonical mapping.</param>
    /// <exception cref="ProtoParseException">The schema could not be parsed.</exception>
    /// <exception cref="ArgumentException">The schema is not valid, or declares no such message.</exception>
    public ProtobufJsonWriter(string protoSchema, string messageTypeName, ProtobufJsonWriterOptions? options = null)
        : this(ProtoSchemaSet.Build(protoSchema), messageTypeName, options) { }

    /// <summary>Rewrites <paramref name="data"/> as compact JSON text.</summary>
    /// <exception cref="InvalidDataException">The payload does not match the schema.</exception>
    public string Write(ReadOnlyMemory<byte> data) {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer)) Write(data, writer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Rewrites <paramref name="data"/> into <paramref name="writer"/>.</summary>
    /// <remarks>
    /// The overload to use for indentation, for a pooled destination, or to place the message inside
    /// a larger document. The writer is not flushed — the caller owns it.
    /// </remarks>
    /// <exception cref="InvalidDataException">The payload does not match the schema.</exception>
    public void Write(ReadOnlyMemory<byte> data, Utf8JsonWriter writer) {
        ArgumentNullException.ThrowIfNull(writer);

        SliceBuffer ??= new Slice[InitialSliceCapacity];

        try {
            WriteMessage(data.Span, _descriptor, writer, depth: 0, offset: 0);
        }
        finally {
            // A single outsized payload must not pin its index to this thread forever.
            if (SliceBuffer.Length > RetainedSliceLimit) SliceBuffer = null;
        }
    }

    /// <summary>Enough for the field count of an ordinary message, grown when a payload needs more.</summary>
    const int InitialSliceCapacity = 64;

    /// <summary>Beyond this, the index is dropped rather than kept for the thread's lifetime.</summary>
    const int RetainedSliceLimit = 4096;

    // One index per thread, shared by every message in a payload. Nesting is strictly stacked, so a
    // level owns the region from its offset and its children take the region above. Renting per
    // message was measured and lost: a deep payload is hundreds of messages, and the pool round trip
    // costs more than the region arithmetic.
    [ThreadStatic] static Slice[]? SliceBuffer;

    /// <summary>Where one occurrence of a field begins, measured from the start of its message.</summary>
    /// <remarks>
    /// The length is not recorded because every encoded value is self-delimiting: a varint ends at
    /// its own continuation bit, a fixed value has a known width, and a length-delimited value
    /// declares its own extent. A reader opened at <paramref name="Start"/> finds the end itself.
    /// </remarks>
    readonly record struct Slice(int Number, int Start, ProtoWireType WireType);

    /// <remarks>
    /// Nesting is decoded by recursion, so the depth a payload declares is the depth this stack
    /// reaches. A recursive message type lets a few bytes ask for thousands of levels, and a stack
    /// overflow cannot be caught — the guard has to be here rather than at the boundary.
    /// </remarks>
    void WriteMessage(ReadOnlySpan<byte> data, MessageDescriptor descriptor, Utf8JsonWriter writer, int depth, int offset) {
        if (depth > _options.MaxDepth)
            throw new InvalidDataException($"The payload nests deeper than {_options.MaxDepth} messages.");

        if (descriptor.FullName == ProtoJson.AnyTypeName) {
            WriteAny(data, writer, depth, offset);
            return;
        }

        if (ProtoJson.IsWellKnown(descriptor) && ProtoJson.TryWriteWellKnown(descriptor, data, writer)) return;

        var count = Index(data, descriptor, offset);

        // Taken after indexing, so it reflects any growth that happened during it. A nested message
        // may grow the buffer again, which leaves this span pointing at the previous array — that
        // array still holds this level's region unchanged, so reading it stays correct.
        var slices = SliceBuffer!.AsSpan(offset, count);

        writer.WriteStartObject();

        foreach (var field in descriptor.Fields.InFieldNumberOrder())
            WriteField(data, field, slices, writer, depth, offset + count);

        writer.WriteEndObject();
    }

    /// <remarks>
    /// Unknown fields are skipped rather than recorded. The canonical mapping has no form for a
    /// field the schema does not declare, and keeping them would only grow the index.
    /// <para>
    /// Emission rescans this array once per declared field rather than following a per-field chain.
    /// Chaining was measured and lost: it costs a head and tail table per message, and a message is
    /// typically two or three fields deep, where the rescan is a handful of comparisons over one
    /// cache line.
    /// </para>
    /// </remarks>
    static int Index(ReadOnlySpan<byte> data, MessageDescriptor descriptor, int offset) {
        var reader = new ProtoWireReader(data);
        var count  = 0;

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number   = ProtoWireReader.FieldNumberOf(tag);
            var wireType = ProtoWireReader.WireTypeOf(tag);
            var start    = reader.Position;

            reader.Skip(wireType, number);

            if (descriptor.FindFieldByNumber(number) is null) continue;

            if (offset + count == SliceBuffer!.Length) Grow();

            SliceBuffer![offset + count++] = new Slice(number, start, wireType);
        }

        return count;

        // The whole used prefix is copied, so every level below keeps its region.
        static void Grow() {
            var larger = new Slice[SliceBuffer!.Length * 2];

            SliceBuffer.CopyTo(larger, 0);

            SliceBuffer = larger;
        }
    }

    void WriteField(ReadOnlySpan<byte> data, FieldDescriptor field, ReadOnlySpan<Slice> slices, Utf8JsonWriter writer, int depth, int offset) {
        var present = false;

        foreach (var slice in slices) {
            if (slice.Number != field.FieldNumber) continue;

            present = true;
            break;
        }

        // The canonical mapping omits an absent field rather than inventing a value for it. A field
        // with implicit presence set to its default is indistinguishable from an absent one on the
        // wire, so it is omitted too — the reader recovers the default from the schema.
        if (!present) return;

        ProtoJson.WriteFieldName(writer, field);

        if (field.IsMap) WriteMap(data, field, slices, writer, depth, offset);
        else if (field.IsRepeated) WriteRepeated(data, field, slices, writer, depth, offset);
        else WriteSingular(data, field, slices, writer, depth, offset);
    }

    void WriteMap(ReadOnlySpan<byte> data, FieldDescriptor field, ReadOnlySpan<Slice> slices, Utf8JsonWriter writer, int depth, int offset) {
        var key   = field.MessageType.FindFieldByNumber(1);
        var value = field.MessageType.FindFieldByNumber(2);

        writer.WriteStartObject();

        foreach (var slice in slices) {
            if (slice.Number != field.FieldNumber) continue;

            var reader = new ProtoWireReader(data[slice.Start..]);

            WriteMapEntry(reader.ReadLengthDelimited(), key, value, writer, depth, offset);
        }

        writer.WriteEndObject();
    }

    /// <remarks>
    /// An entry omits <c>key</c> or <c>value</c> when either is its zero, so both default rather
    /// than fault. That is what every other implementation reads back.
    /// </remarks>
    void WriteMapEntry(ReadOnlySpan<byte> entry, FieldDescriptor key, FieldDescriptor value, Utf8JsonWriter writer, int depth, int offset) {
        var keyStart   = -1;
        var valueStart = -1;

        var reader = new ProtoWireReader(entry);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number   = ProtoWireReader.FieldNumberOf(tag);
            var wireType = ProtoWireReader.WireTypeOf(tag);
            var start    = reader.Position;

            reader.Skip(wireType, number);

            if (number == 1) keyStart = start;
            else if (number == 2) valueStart = start;
        }

        if (keyStart < 0) writer.WritePropertyName(DefaultMapKey(key));
        else WriteMapKey(entry[keyStart..], key, writer);

        if (valueStart < 0) ProtoJson.WriteDefault(writer, value);
        else WriteValue(entry[valueStart..], value, writer, depth, offset);
    }

    /// <remarks>
    /// A JSON object key is always a string, so an integral or boolean key is spelled out. The
    /// digits are formatted straight to UTF-8 rather than through a <see cref="string"/>, because a
    /// map field pays this once per entry.
    /// </remarks>
    void WriteMapKey(ReadOnlySpan<byte> data, FieldDescriptor key, Utf8JsonWriter writer) {
        var reader = new ProtoWireReader(data);

        if (key.FieldType == Type.String) {
            writer.WritePropertyName(reader.ReadLengthDelimited());
            return;
        }

        if (key.FieldType == Type.Bool) {
            writer.WritePropertyName(reader.ReadVarint() != 0 ? "true" : "false");
            return;
        }

        Span<byte> buffer = stackalloc byte[ProtoJson.Int64BufferSize];

        var written = key.FieldType switch {
            Type.Int32 or Type.Int64      => ProtoJson.Format(buffer, (long)reader.ReadVarint()),
            Type.UInt32 or Type.UInt64    => ProtoJson.Format(buffer, reader.ReadVarint()),
            Type.SInt32 or Type.SInt64    => ProtoJson.Format(buffer, ProtoWireReader.DecodeZigZag64(reader.ReadVarint())),
            Type.Fixed32 or Type.SFixed32 => ProtoJson.Format(buffer, (int)reader.ReadFixed32()),
            Type.Fixed64 or Type.SFixed64 => ProtoJson.Format(buffer, (long)reader.ReadFixed64()),

            _ => throw new InvalidDataException($"Map key '{key.FullName}' has type {key.FieldType}, which cannot key a map.")
        };

        writer.WritePropertyName(buffer[..written]);
    }

    static string DefaultMapKey(FieldDescriptor key) =>
        key.FieldType switch {
            Type.String => "",
            Type.Bool   => "false",
            _           => "0"
        };

    void WriteRepeated(ReadOnlySpan<byte> data, FieldDescriptor field, ReadOnlySpan<Slice> slices, Utf8JsonWriter writer, int depth, int offset) {
        writer.WriteStartArray();

        foreach (var slice in slices) {
            if (slice.Number != field.FieldNumber) continue;

            var reader = new ProtoWireReader(data[slice.Start..]);

            // A packed run carries every element in one length-delimited block. The same field can
            // also appear unpacked in the same message, so the wire type decides per occurrence.
            if (slice.WireType == ProtoWireType.LengthDelimited && ProtoJson.IsPackable(field.FieldType)) {
                var packed = reader.ReadLengthDelimited();
                var inner  = new ProtoWireReader(packed);

                while (!inner.IsAtEnd) ProtoJson.WriteScalar(ref inner, field, writer);

                continue;
            }

            WriteValue(data[slice.Start..], field, writer, depth, offset);
        }

        writer.WriteEndArray();
    }

    void WriteSingular(ReadOnlySpan<byte> data, FieldDescriptor field, ReadOnlySpan<Slice> slices, Utf8JsonWriter writer, int depth, int offset) {
        var start = -1;

        // Last occurrence wins. The format allows a singular field to repeat, and every
        // implementation keeps the final value.
        foreach (var slice in slices) {
            if (slice.Number == field.FieldNumber)
                start = slice.Start;
        }

        WriteValue(data[start..], field, writer, depth, offset);
    }

    void WriteValue(ReadOnlySpan<byte> data, FieldDescriptor field, Utf8JsonWriter writer, int depth, int offset) {
        var reader = new ProtoWireReader(data);

        switch (field.FieldType) {
            case Type.Message:
                WriteMessage(reader.ReadLengthDelimited(), field.MessageType, writer, depth + 1, offset);
                break;

            case Type.Group:
                WriteMessage(reader.ReadGroup(field.FieldNumber), field.MessageType, writer, depth + 1, offset);
                break;

            default:
                ProtoJson.WriteScalar(ref reader, field, writer);
                break;
        }
    }

    /// <remarks>
    /// The payload's type is named at run time by <c>type_url</c>, so it is resolved against the
    /// schema set. A type the set does not declare cannot be expanded; the mapping has no form for
    /// that, so the raw bytes are emitted under <c>value</c> rather than the message being dropped.
    /// </remarks>
    void WriteAny(ReadOnlySpan<byte> data, Utf8JsonWriter writer, int depth, int offset) {
        var typeUrl = "";

        var payloadStart = -1;
        var reader       = new ProtoWireReader(data);

        while (!reader.IsAtEnd) {
            var tag = reader.ReadTag();

            if (tag == 0) break;

            var number   = ProtoWireReader.FieldNumberOf(tag);
            var wireType = ProtoWireReader.WireTypeOf(tag);

            if (number == 1 && wireType == ProtoWireType.LengthDelimited) {
                typeUrl = reader.ReadString();
            }
            else if (number == 2 && wireType == ProtoWireType.LengthDelimited) {
                payloadStart = reader.Position;
                reader.Skip(wireType, number);
            }
            else {
                reader.Skip(wireType, number);
            }
        }

        var separator   = typeUrl.LastIndexOf('/');
        var messageName = separator < 0 ? typeUrl : typeUrl[(separator + 1)..];
        var descriptor  = messageName.Length == 0 ? null : _schema.TryFindMessage(messageName);

        var payload = payloadStart < 0
            ? ReadOnlySpan<byte>.Empty
            : new ProtoWireReader(data[payloadStart..]).ReadLengthDelimited();

        if (descriptor is null) {
            writer.WriteStartObject();
            writer.WriteString("@type", typeUrl);
            writer.WriteBase64String("value", payload);
            writer.WriteEndObject();

            return;
        }

        // A well-known payload has no fields to merge into the wrapper, so the mapping nests it
        // under `value` instead of flattening it.
        if (ProtoJson.IsWellKnown(descriptor)) {
            writer.WriteStartObject();
            writer.WriteString("@type", typeUrl);
            writer.WritePropertyName("value");

            WriteMessage(payload, descriptor, writer, depth + 1, offset);

            writer.WriteEndObject();

            return;
        }

        var count  = Index(payload, descriptor, offset);
        var slices = SliceBuffer!.AsSpan(offset, count);

        writer.WriteStartObject();
        writer.WriteString("@type", typeUrl);

        foreach (var field in descriptor.Fields.InFieldNumberOrder())
            WriteField(payload, field, slices, writer, depth, offset + count);

        writer.WriteEndObject();
    }
}
