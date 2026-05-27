#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Diagnostics;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Client;
using KurrentDB.Protocol.V1;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Streaming.Models;


// public static class RecordPositionExtensions {
//     public static Position? ToPosition(this RecordPosition position) =>
//         position.LogPosition.ToPosition();
//
//     static Position? ToPosition(this LogPosition position) {
//         if (position == LogPosition.Earliest)
//             return null;
//
//         if (position == LogPosition.Latest)
//             return Position.End;
//
//         return new Position(position.CommitPosition!.Value, position.PreparePosition!.Value);
//     }
// }


static class ProtocolMapping {
    public static async ValueTask<Record> MapToRecordAsync(
        this Contracts.ReadResp.Types.ReadEvent readEvent,
        SequenceId sequenceId,
        ISchemaSerializerProvider serializerProvider,
        IMetadataDecoder metadataDecoder,
        SchemaRegistryPolicy registryPolicy,
        bool skipDecoding = false,
        CancellationToken ct = default
    ) {
        // The server always populates at least one of Event or Link:
        //   Event + Link → resolved link (record is the event, link carries source metadata)
        //   Link only    → unresolved link (record IS the link itself)
        //   Event only   → regular event
        var recorded = readEvent.Event ?? readEvent.Link
            ?? throw new UnreachableException("ReadEvent has neither Event nor Link.");

        var isLink = readEvent.Event is null;

        var link = readEvent is { Link: { } linkEvent, Event: not null }
            ? new Link {
                Name     = linkEvent.Metadata[Constants.Metadata.Type],
                Position = (long)linkEvent.CommitPosition,
                Revision = (long)linkEvent.StreamRevision,
                Stream   = StreamName.From(linkEvent.StreamIdentifier!),
            }
            : Link.Unset;

        var stream    = StreamName.From(recorded.StreamIdentifier);
        var recordId  = Uuid.FromDto(recorded.Id).ToGuid();
        var revision  = Revision.From((long)recorded.StreamRevision);
        var position  = LogPosition.From((long)recorded.CommitPosition);
        var data      = recorded.Data.Memory;
        var timestamp = Convert.ToInt64(recorded.Metadata[Constants.Metadata.Created]).FromTicksSinceEpoch();

        // for v1 this is super weird because we have schema info in metadata
        // not needed anymore specially in v2 where we have schema as a first class concept
        var schemaFormat = recorded.Metadata[Constants.Metadata.ContentType].GetSchemaDataFormat(); // it will always be json or octet-stream
        var schemaName   = SchemaName.From(recorded.Metadata[Constants.Metadata.Type]);

        var metadata = await metadataDecoder
            .Decode(recorded.CustomMetadata.Memory, new MetadataDecoderContext(stream, schemaName, schemaFormat))
            .ConfigureAwait(false);

        var schema = metadata.ExtractSchemaInfo();

        IRecordDecoder decoder = new RecordDecoder(serializerProvider, registryPolicy);

        var record = new Record(decoder) {
            Id        = recordId,
            Position  = RecordPosition.From(position, stream, revision),
            Sequence  = sequenceId,
            Timestamp = timestamp,
            Data      = data,
            Metadata  = metadata,
            Schema    = schema,
            Link      = link,
            IsLink    = isLink
        };

        if (!skipDecoding)
            await record.TryDecode(ct).ConfigureAwait(false);

        return record;
    }
}
