#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Buffers;
using System.Diagnostics;
using Kurrent.Client.Streams;
using Kurrent.Schema;

namespace Kurrent.Client.Schema.Serialization.Bytes;

/// <summary>
/// Provides a passthrough implementation of the <see cref="ISchemaSerializer"/> interface
/// for the <see cref="SchemaDataFormat.Bytes"/> format. This serializer does not transform
/// the raw data and assumes the input value is an array or memory structure of bytes.
/// <para />
///  It will enforce the schema data format to be <see cref="SchemaDataFormat.Bytes"/>.
/// </summary>
public class BytesPassthroughSerializer : ISchemaSerializer {
	public SchemaDataFormat DataFormat => SchemaDataFormat.Bytes;

	public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SchemaSerializationContext context) {
		Debug.Assert(
            value is null or byte[] or ReadOnlyMemory<byte> or Memory<byte> or ReadOnlySequence<byte> or ArraySegment<byte>,
            "value must be byte[], ReadOnlyMemory<byte>, Memory<byte>, ReadOnlySequence<byte>, or ArraySegment<byte>");

		// enforce the schema data format
        context.RecordSchema = context.RecordSchema with {
            DataFormat = SchemaDataFormat.Bytes
        };

		var result = value switch {
			null                       => ReadOnlyMemory<byte>.Empty,
			byte[] bytes               => bytes,
			ReadOnlyMemory<byte> bytes => bytes,
			Memory<byte> bytes         => bytes,
            ReadOnlySequence<byte> seq => seq.ToArray(),
            ArraySegment<byte> seg     => seg.AsMemory()
		};

		return new(result);
	}

    public ValueTask<DeserializationResult> Deserialize(ReadOnlyMemory<byte> data, SchemaSerializationContext context) {
        if (context.RecordSchema.Schema == LinkTo.SchemaName)
            return new(new DeserializationResult.Deserialized(LinkTo.Parse(data.Span)));

        return new(new DeserializationResult.Deserialized(data));
    }
}
