using System.Buffers;
using System.Text;
using System.Text.Json;
using Kurrent.Surge.Codecs.Avro.Binary;
using Kurrent.Surge.Codecs.Avro.Generators.Model;

namespace Kurrent.Surge.Codecs.Avro;

/// <summary>
/// Rewrites an Avro binary payload as JSON, guided by the schema that wrote it.
/// </summary>
/// <remarks>
/// Implements the Avro specification, "JSON Encoding" —
/// https://avro.apache.org/docs/1.12.0/specification/#json-encoding
/// <para>
/// Nothing is decoded into a CLR type on the way, so this reads a payload whose message type is not
/// compiled into the process. The schema is the only thing required, and it is parsed once per instance.
/// </para>
/// <para>
/// Apache offers the same transcode through <c>GenericDatumReader</c> and <c>JsonEncoder</c>. Both route
/// through <c>Avro.Schema.Parse</c> and <c>Newtonsoft.Json.JsonTextWriter</c>, which links Newtonsoft
/// into the binary and costs about 9.5 MB under Native AOT. Agreement with that path is asserted for
/// every construct in <c>Kurrent.Surge.Codecs.Avro.Tests</c>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class AvroJsonWriter {
    /// <summary>Beyond this a byte string is rented rather than stack-allocated for widening.</summary>
    const int StackLimit = 256;

    readonly AvroSchemaDocument _schema;

    /// <param name="schema">The writer's schema, as Avro schema JSON.</param>
    /// <exception cref="ArgumentException">The schema is not valid Avro.</exception>
    public AvroJsonWriter(string schema) {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);

        if (!AvroSchemaParser.TryParse(schema, out var document, out var error, out var offset))
            throw new ArgumentException($"The Avro schema could not be parsed at offset {offset}: {error}", nameof(schema));

        _schema = document;
    }

    /// <summary>Rewrites <paramref name="data"/> as compact JSON text.</summary>
    /// <exception cref="InvalidDataException">The payload does not match the schema.</exception>
    public string Write(ReadOnlyMemory<byte> data) {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
            Write(data, writer);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Rewrites <paramref name="data"/> into <paramref name="writer"/>.</summary>
    /// <remarks>
    /// The overload to use for indentation, for a pooled destination, or to place the value inside a
    /// larger document. The writer is not flushed — the caller owns it.
    /// </remarks>
    /// <exception cref="InvalidDataException">The payload does not match the schema.</exception>
    public void Write(ReadOnlyMemory<byte> data, Utf8JsonWriter writer) {
        ArgumentNullException.ThrowIfNull(writer);

        WriteValue(_schema.Root, new AvroBinaryDecoder(data), writer);
    }

    void WriteValue(AvroSchema schema, AvroBinaryDecoder decoder, Utf8JsonWriter writer) {
        switch (_schema.Resolve(schema)) {
            case AvroPrimitive primitive:
                WritePrimitive(primitive.Kind, decoder, writer);
                break;

            // A logical type does not change the JSON encoding: the value is written as its base type,
            // which is what Apache's own JsonEncoder emits, so a `date` is a day count and not a date.
            case AvroEnum @enum:
                writer.WriteStringValue(Symbol(@enum, decoder.ReadEnum()));
                break;

            case AvroFixed @fixed:
                WriteByteString(decoder.ReadFixedSpan(@fixed.Size), writer);
                break;

            case AvroRecord record:
                writer.WriteStartObject();

                foreach (var field in record.Fields) {
                    writer.WritePropertyName(field.Name);
                    WriteValue(field.Type, decoder, writer);
                }

                writer.WriteEndObject();
                break;

            case AvroArray array:
                writer.WriteStartArray();

                // Blocked: a count, that many items, repeating until a zero count. The JSON form is one
                // flat array, so the blocks are joined rather than represented.
                for (var count = decoder.ReadArrayStart(); count != 0; count = decoder.ReadArrayNext())
                    for (var i = 0L; i < count; i++)
                        WriteValue(array.Items, decoder, writer);

                writer.WriteEndArray();
                break;

            case AvroMap map:
                writer.WriteStartObject();

                for (var count = decoder.ReadMapStart(); count != 0; count = decoder.ReadMapNext())
                    for (var i = 0L; i < count; i++) {
                        writer.WritePropertyName(decoder.ReadString());
                        WriteValue(map.Values, decoder, writer);
                    }

                writer.WriteEndObject();
                break;

            case AvroUnion union:
                WriteUnion(union, decoder, writer);
                break;
        }
    }

    /// <remarks>
    /// The null branch is a bare JSON null. Every other branch is wrapped in an object whose single
    /// property is the branch's type name, because JSON alone cannot say which branch was written.
    /// </remarks>
    void WriteUnion(AvroUnion union, AvroBinaryDecoder decoder, Utf8JsonWriter writer) {
        var index = decoder.ReadUnionIndex();

        if ((uint)index >= (uint)union.Branches.Count)
            throw new InvalidDataException($"The payload selected union branch {index}, but the schema declares {union.Branches.Count}");

        var branch = union.Branches[index];

        if (_schema.Resolve(branch) is AvroPrimitive { Kind: AvroPrimitiveKind.Null }) {
            decoder.ReadNull();
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName(BranchName(branch));
        WriteValue(branch, decoder, writer);
        writer.WriteEndObject();
    }

    string Symbol(AvroEnum @enum, int ordinal) =>
        (uint)ordinal < (uint)@enum.Symbols.Count
            ? @enum.Symbols[ordinal]
            : throw new InvalidDataException($"The payload selected symbol {ordinal} of enum '{@enum.FullName}', which declares {@enum.Symbols.Count}");

    static void WritePrimitive(AvroPrimitiveKind kind, AvroBinaryDecoder decoder, Utf8JsonWriter writer) {
        switch (kind) {
            case AvroPrimitiveKind.Null:    decoder.ReadNull(); writer.WriteNullValue();                  break;
            case AvroPrimitiveKind.Boolean: writer.WriteBooleanValue(decoder.ReadBoolean());              break;
            case AvroPrimitiveKind.Int:     writer.WriteNumberValue(decoder.ReadInt());                   break;
            case AvroPrimitiveKind.Long:    writer.WriteNumberValue(decoder.ReadLong());                  break;
            case AvroPrimitiveKind.Float:   writer.WriteNumberValue(decoder.ReadFloat());                 break;
            case AvroPrimitiveKind.Double:  writer.WriteNumberValue(decoder.ReadDouble());                break;
            case AvroPrimitiveKind.Bytes:   WriteByteString(decoder.ReadBytesSpan(), writer);             break;
            case AvroPrimitiveKind.String:  writer.WriteStringValue(decoder.ReadString());                break;
        }
    }

    /// <summary>Writes <c>bytes</c> and <c>fixed</c> as the specification does.</summary>
    /// <remarks>
    /// Each byte becomes the Unicode code point of the same value, so <c>0xFF</c> is U+00FF. This is not
    /// Base64, which is the assumption that breaks when such a value is read back by another
    /// implementation.
    /// </remarks>
    static void WriteByteString(ReadOnlySpan<byte> value, Utf8JsonWriter writer) {
        char[]? rented = null;

        var buffer = value.Length <= StackLimit
            ? stackalloc char[StackLimit]
            : (rented = ArrayPool<char>.Shared.Rent(value.Length)).AsSpan();

        try {
            for (var i = 0; i < value.Length; i++) buffer[i] = (char)value[i];

            writer.WriteStringValue(buffer[..value.Length]);
        }
        finally {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>The name a union branch is tagged with.</summary>
    /// <remarks>A named type is tagged with its fullname; everything else with its Avro type name.</remarks>
    static string BranchName(AvroSchema branch) => branch switch {
        AvroNamedReference reference => reference.FullName,
        IAvroNamed named             => named.FullName,
        AvroArray                    => "array",
        AvroMap                      => "map",
        AvroPrimitive primitive      => PrimitiveName(primitive.Kind),

        // The specification forbids a union as a direct branch of another union.
        _ => throw new InvalidDataException($"A union cannot declare a branch of type {branch.GetType().Name}")
    };

    static string PrimitiveName(AvroPrimitiveKind kind) => kind switch {
        AvroPrimitiveKind.Null    => "null",
        AvroPrimitiveKind.Boolean => "boolean",
        AvroPrimitiveKind.Int     => "int",
        AvroPrimitiveKind.Long    => "long",
        AvroPrimitiveKind.Float   => "float",
        AvroPrimitiveKind.Double  => "double",
        AvroPrimitiveKind.Bytes   => "bytes",
        _                         => "string"
    };
}
