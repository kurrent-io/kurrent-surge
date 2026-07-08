using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

/// <summary>
/// Maps wire protocol events to domain <see cref="Record"/> instances.
/// Captures serialization dependencies at construction so callers don't
/// need to thread them through every call.
/// </summary>
sealed class RecordMapper(
    ISchemaSerializerProvider serializerProvider,
    IMetadataDecoder metadataDecoder,
    bool skipDecoding
) {
    public ValueTask<Record> MapAsync(Contracts.ReadResp.Types.ReadEvent readEvent, SequenceId sequenceId, CancellationToken ct) =>
        readEvent.MapToRecordAsync(
            sequenceId,
            new MappingContext(serializerProvider, metadataDecoder, SchemaRegistryPolicy.NoRequirements, ct),
            skipDecoding);
}
