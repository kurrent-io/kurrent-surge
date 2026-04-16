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

namespace Kurrent.Processors.Models;


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
    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new(); // old was { Structured = new() }
    static readonly Empty                                                     DefaultEmpty               = new();

    internal static class Requests {
        public static Contracts.ReadReq CreateLogSubscriptionRequest(IRecordFilter filter, LogPosition startFrom) {
            Debug.Assert(!filter.IsStreamFilter, "Stream filters should not reach CreateLogSubscriptionRequest.");
            Debug.Assert(startFrom != LogPosition.Unset, "Start position must be resolved before reaching the mapper.");

            var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
                ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                Subscription  = DefaultSubscriptionOptions,
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                All           = ConvertToAllOptions(startFrom),
            }};

            if (ConvertToFilterOptions(filter) is { } options)
                req.Options.Filter = options;
            else
                req.Options.NoFilter = DefaultEmpty;

            return req;
        }

        public static Contracts.ReadReq CreateStreamSubscriptionRequest(IRecordFilter filter, Revision startFrom) {
            Debug.Assert(filter.IsStreamFilter, "Only stream filters should reach CreateStreamSubscriptionRequest.");
            Debug.Assert(startFrom != Revision.Unset, "Start revision must be resolved before reaching the mapper.");

            var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
                ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                Subscription  = DefaultSubscriptionOptions,
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                Stream        = ConvertToStreamOptions(filter.TargetStream, startFrom),
                NoFilter      = DefaultEmpty
            }};

            return req;
        }

        static Contracts.ReadReq.Types.Options.Types.AllOptions ConvertToAllOptions(LogPosition start) {
            Debug.Assert(start != LogPosition.Unset, "Start position must be resolved before reaching the mapper.");

            return start switch {
                _ when start == LogPosition.Latest   => new() { End   = DefaultEmpty },
                _ when start == LogPosition.Earliest => new() { Start = DefaultEmpty },
                _ => new() {
                    Position = new() {
                        CommitPosition  = start,
                        PreparePosition = start
                    }
                }
            };
        }

        static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(StreamName stream, Revision start) {
            Debug.Assert(stream != StreamName.Unset, "Stream name must be resolved before reaching the mapper.");
            Debug.Assert(start != Revision.Unset, "Start revision must be resolved before reaching the mapper.");

            return start switch {
                _ when start == Revision.Max => new() { StreamIdentifier = stream.Value, End      = DefaultEmpty },
                _ when start == Revision.Min => new() { StreamIdentifier = stream.Value, Start    = DefaultEmpty },
                _                            => new() { StreamIdentifier = stream.Value, Revision = (ulong)start.Value }
            };
        }

        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
                StreamFilter f => new() { StreamIdentifier = new() { Regex = f.Expression } },

                ExpressionFilter { ServerFilter: StreamFilter f } => new() { StreamIdentifier = new() { Regex = f.Expression } },

                _ => null
            };

            if (options is null) return null;

            options.Max = uint.MaxValue; // delay as much as we can - will fix on v2

            options.CheckpointIntervalMultiplier = 1;

            return options;
        }
    }

    public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) =>
        new(CheckpointId.New(), CheckpointTrigger.Interval, checkpoint.Timestamp.ToDateTimeOffset(), PartitionPositions.Empty);

    public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) =>
        new(CheckpointId.New(), CheckpointTrigger.CaughtUp, caughtUp.Timestamp.ToDateTimeOffset(), PartitionPositions.Empty);

    public static async ValueTask<Record> MapToRecordAsync(
        this Contracts.ReadResp.Types.ReadEvent readEvent,
        SequenceId sequenceId,
        ISchemaSerializerProvider serializerProvider,
        IMetadataDecoder metadataDecoder,
        SchemaRegistryPolicy registryPolicy,
        bool skipDecoding = false,
        CancellationToken ct = default
    ) {
        var recordedEvent = readEvent.Event;

        // first parse all info
        var stream    = StreamName.From(recordedEvent.StreamIdentifier);
        var recordId  = Uuid.FromDto(recordedEvent.Id).ToGuid();
        var revision  = Revision.From((long)recordedEvent.StreamRevision);
        var position  = LogPosition.From((long)recordedEvent.CommitPosition);
        var data      = recordedEvent.Data.Memory;
        var timestamp = Convert.ToInt64(recordedEvent.Metadata[Constants.Metadata.Created]).FromTicksSinceEpoch();

        // for v1 this is super weird because we have schema info in metadata
        // not needed anymore specially in v2 where we have schema as a first class concept
        var schemaFormat = recordedEvent.Metadata[Constants.Metadata.ContentType].GetSchemaDataFormat(); // it will always be json or octet-stream
        var schemaName   = SchemaName.From(recordedEvent.Metadata[Constants.Metadata.Type]);

        var metadata = await metadataDecoder
            .Decode(recordedEvent.CustomMetadata.Memory, new MetadataDecoderContext(stream, schemaName, schemaFormat))
            .ConfigureAwait(false);

        var schema = metadata.ExtractSchemaInfo();

        var recordPosition = RecordPosition.From(position, stream, revision);

        // var link = readEvent.Link is { } linkEvent
        //     ? new Link {
        //         Name     = linkEvent.Metadata[Constants.Metadata.Type],
        //         Position = (long)linkEvent.CommitPosition,
        //         Revision = (long)linkEvent.StreamRevision,
        //         Stream   = StreamName.From(linkEvent.StreamIdentifier!),
        //     }
        //     : Link.Unset;

        // var indexRevision = readEvent.HasCommitPosition
        //     ? Revision.From((long)readEvent.CommitPosition)
        //     : link.Revision == Revision.Unset ? revision : link.Revision;

        // create a decoder
        IRecordDecoder decoder = new RecordDecoder(serializerProvider, registryPolicy);

        // and we are done
        var record = new Record(decoder) {
            Id             = recordId,
            Position       = recordPosition,
            Sequence       = sequenceId,
            Timestamp      = timestamp,
            Data           = data,
            Metadata       = metadata,
            Schema         = schema,

            // // what? not sure...
            // Link           = link,
            // IndexRevision  = Revision.Unset // indexRevision
        };

        // now decode the record if required
        if (!skipDecoding)
            await record.TryDecode(ct).ConfigureAwait(false);

        return record;
    }
}
