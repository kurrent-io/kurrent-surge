#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Buffers;
using System.Diagnostics;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Client.Schema.Serialization.Bytes;

/// <summary>
/// Provides a passthrough implementation of the <see cref="ISchemaSerializer"/> interface
/// for the <see cref="SchemaDataFormat.Bytes"/> format. This serializer does not transform
/// the raw data and assumes the input value is an array or memory structure of bytes.
/// </summary>
/// <remarks>
/// Reached only for <see cref="SchemaDataFormat.Bytes"/>: the serializer provider dispatches by
/// format, so the schema format is already Bytes on entry and is not re-stamped here.
/// </remarks>
public class BytesPassthroughSerializer : ISchemaSerializer {
	public SchemaDataFormat DataFormat => SchemaDataFormat.Bytes;

	public ValueTask<ReadOnlyMemory<byte>> Serialize(object? value, SchemaSerializationContext context) {
		Debug.Assert(
            value is null or byte[] or ReadOnlyMemory<byte> or Memory<byte> or ReadOnlySequence<byte> or ArraySegment<byte>,
            "value must be byte[], ReadOnlyMemory<byte>, Memory<byte>, ReadOnlySequence<byte>, or ArraySegment<byte>");

		// The provider dispatches by format (1:1), so this serializer is only ever reached for Bytes —
		// the context already carries Bytes. Assert the invariant instead of re-stamping it, which would
		// allocate a fresh RecordSchemaInfo on every call.
		Debug.Assert(
			context.RecordSchema.DataFormat == SchemaDataFormat.Bytes,
			"BytesPassthroughSerializer reached with a non-Bytes schema format");

		var result = value switch {
            byte[] bytes               => bytes,
			ReadOnlyMemory<byte> bytes => bytes,
			Memory<byte> bytes         => bytes,
            ReadOnlySequence<byte> seq => seq.IsSingleSegment ? seq.First : seq.ToArray(),
            ArraySegment<byte> seg     => seg.AsMemory(),
            null                       => ReadOnlyMemory<byte>.Empty,
		};

		return new(result);
	}

    public ValueTask<DeserializationResult> Deserialize(ReadOnlyMemory<byte> data, SchemaSerializationContext context) =>
        context.RecordSchema.Schema == LinkTo.SchemaName
            ? new(new DeserializationResult.Deserialized(LinkTo.Parse(data.Span)))
            : new(new DeserializationResult.Deserialized(data));
}
