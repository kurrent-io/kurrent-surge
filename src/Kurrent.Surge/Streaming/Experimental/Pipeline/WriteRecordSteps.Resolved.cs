using System.Buffers;
using System.Runtime.CompilerServices;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Kurrent.Surge.Schema;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Typestate carrier between resolve and encode: the source record and its resolved wire schema.
/// </summary>
readonly record struct ResolvedWrite(AppendRecord Source, RecordSchemaInfo Schema);

sealed class ResolveWriteStep(SchemaResolver resolver) : IStep<AppendRecord, ResolvedWrite> {
    public async ValueTask<ResolvedWrite> Execute(AppendRecord input, CancellationToken ct) {
        // Raw payloads have no type mapping to resolve — the frame step names them from the record.
        if (input.DataFormat == SchemaDataFormat.Bytes)
            return new(input, new RecordSchemaInfo { DataFormat = SchemaDataFormat.Bytes });

        var schema = await resolver
            .ResolveWriteSchema(input.Value.GetType(), input.Stream, input.DataFormat, ct)
            .ConfigureAwait(false);

        return new(input, schema);
    }
}

/// <summary>
/// Pure encode over an already-resolved schema. Base-derived serializers run through their
/// synchronous core — the async surface re-resolves the schema, which the split exists to
/// avoid. Bytes unwraps inline, mirroring the passthrough serializer's switch.
/// </summary>
sealed class EncodeResolvedWriteStep(ISchemaSerializerProvider serializerProvider) : IStep<ResolvedWrite, EncodedWrite> {
    public ValueTask<EncodedWrite> Execute(ResolvedWrite input, CancellationToken ct) {
        var source = input.Source;

        if (source.DataFormat == SchemaDataFormat.Bytes)
            return new(new EncodedWrite(source, input.Schema, UnwrapBytes(source.Value)));

        var serializer = (SchemaSerializerBase)serializerProvider.GetSerializer(source.DataFormat);

        try {
            var payload = source.Value is null
                ? ReadOnlyMemory<byte>.Empty
                : SerializeCore(serializer, source.Value);

            return new(new EncodedWrite(source, input.Schema, payload));
        }
        // Mirrors the serializer base's wrap so the split keeps the same error contract.
        catch (Exception ex) when (ex is not SchemaSerializationException) {
            throw new SchemaSerializationFailedException(source.DataFormat, source.Value!.GetType(), ex);
        }
    }

    // Kept in sync with BytesPassthroughSerializer.Serialize.
    static ReadOnlyMemory<byte> UnwrapBytes(object? value) =>
        value switch {
            byte[] bytes               => bytes,
            ReadOnlyMemory<byte> bytes => bytes,
            Memory<byte> bytes         => bytes,
            ReadOnlySequence<byte> seq => seq.IsSingleSegment ? seq.First : seq.ToArray(),
            ArraySegment<byte> seg     => seg.AsMemory(),
            null                       => ReadOnlyMemory<byte>.Empty,
            _                          => throw new InvalidOperationException($"Bytes format requires a binary payload, got '{value.GetType().Name}'.")
        };

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Serialize")]
    static extern ReadOnlyMemory<byte> SerializeCore(SchemaSerializerBase serializer, object? value);
}
