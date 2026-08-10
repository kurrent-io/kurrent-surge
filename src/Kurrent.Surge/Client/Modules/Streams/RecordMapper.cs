using Kurrent.Surge.Client.Streams.Checkpoints;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Maps wire protocol events to domain <see cref="Record"/> instances.
/// Captures the decoder and metadata decoder at construction so callers don't
/// need to thread them through every call.
/// </summary>
sealed class RecordMapper(
    IRecordDecoder decoder,
    IMetadataDecoder metadataDecoder,
    bool skipDecoding
) {
    public ValueTask<Record> MapAsync(Contracts.ReadResp.Types.ReadEvent readEvent, SequenceId sequenceId, CancellationToken ct) =>
        readEvent.MapToRecordAsync(
            sequenceId,
            new MappingContext(metadataDecoder, decoder, ct),
            skipDecoding);
}
