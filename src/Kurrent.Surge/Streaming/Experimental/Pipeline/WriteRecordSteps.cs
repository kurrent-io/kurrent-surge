using Google.Protobuf;
using Kurrent.Surge.Client.Schema;
using Kurrent.Surge.Client.Schema.Serialization;
using Kurrent.Surge.Client.Streams;
using Contracts = KurrentDB.Protocol.V2.Streams;

namespace Kurrent.Surge.Experimental;

/// <summary>
/// Typestate carrier between encode and frame: the source record, the schema the serializer
/// resolved, and the serialized payload.
/// </summary>
readonly record struct EncodedWrite(AppendRecord Source, RecordSchemaInfo Schema, ReadOnlyMemory<byte> Payload);

sealed class ValidateWriteStep : IStep<AppendRecord, AppendRecord> {
    public ValueTask<AppendRecord> Execute(AppendRecord input, CancellationToken ct) {
        input.EnsureValid();
        return new(input);
    }
}

/// <summary>
/// Serializes on today's seam: the serializer resolves the schema internally and returns it
/// through the context's write-back, so this stage carries the completed schema out via the
/// typestate. Splits into Resolve + Encode when schema resolution moves out of the serializer.
/// </summary>
sealed class EncodeWriteStep(ISchemaSerializerProvider serializerProvider, SchemaRegistryPolicy policy) : IStep<AppendRecord, EncodedWrite> {
    public async ValueTask<EncodedWrite> Execute(AppendRecord input, CancellationToken ct) {
        var context = new SchemaSerializationContext {
            Stream               = input.Stream,
            RecordSchema         = new() { DataFormat = input.DataFormat },
            SchemaRegistryPolicy = policy,
            CancellationToken    = ct
        };

        var payload = await serializerProvider
            .GetSerializer(input.DataFormat)
            .Serialize(input.Value, context)
            .ConfigureAwait(false);

        return new EncodedWrite(input, context.RecordSchema, payload);
    }
}

sealed class FrameWriteStep : IStep<EncodedWrite, Contracts.AppendRecord> {
    public ValueTask<Contracts.AppendRecord> Execute(EncodedWrite input, CancellationToken ct) {
        var source = input.Source;

        // UnsafeWrap constructs a new ByteString from the given bytes. The bytes are not
        // copied, and must not be modified while the ByteString is in use.
        var contract = new Contracts.AppendRecord {
            // The wire expects the GUID string form — RecordId.ToString() is the ULID face.
            RecordId = source.RecordId.ToGuidString(),
            Data     = UnsafeByteOperations.UnsafeWrap(input.Payload),
            Schema   = new Contracts.SchemaInfo {
                Id     = input.Schema.HasSchemaId ? input.Schema.VersionId.ToString() : Guid.NewGuid().ToString(),
                Name   = source.SchemaName ?? input.Schema.Schema,
                Format = (Contracts.SchemaFormat)input.Schema.DataFormat
            },
            Stream   = source.Stream
        };

        if (source.Metadata.Count > 0)
            source.Metadata.CopyToProperties(contract.Properties);

        return new(contract);
    }
}
