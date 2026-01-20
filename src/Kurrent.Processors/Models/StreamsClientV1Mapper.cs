#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

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

static class ProtocolMapping {
    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly Empty                                                     DefaultEmpty               = new();

    internal static class Requests {
        /// <summary>
        /// Creates a request to subscribe to records based on the provided processing options.
        /// </summary>
        public static Contracts.ReadReq CreateSubscriptionRequest(ProcessorSettings options) {
            var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
                Subscription  = DefaultSubscriptionOptions,
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards
            }};

            var subscription = options.Subscription;
            var heartbeat    = new HeartbeatOptions(true, options.Checkpoint.Threshold);

            // Convert StartPosition (long) to the appropriate position type
            var logPosition = subscription.StartPosition switch {
                0 when subscription.InitialPosition == SubscriptionInitialPosition.Earliest => LogPosition.Earliest,
                0 when subscription.InitialPosition == SubscriptionInitialPosition.Latest   => LogPosition.Latest,
                0                                                                           => LogPosition.Earliest,
                _                                                                           => LogPosition.From(subscription.StartPosition)
            };

            var streamRevision = subscription.StartPosition switch {
                0 => StreamRevision.Min,
                _ => StreamRevision.From(subscription.StartPosition)
            };

            if (subscription.Filter == RecordFilter.None)
                req.Options.NoFilter = DefaultEmpty;
            else if (subscription.Filter.TargetStream != StreamName.None && subscription.Filter.TargetStream != StreamName.All) {
                req.Options.Stream   = ConvertToStreamOptions(subscription.Filter.TargetStream, streamRevision);
                req.Options.NoFilter = DefaultEmpty;
            }
            else {
                req.Options.All    = ConvertToAllOptions(logPosition);
                req.Options.Filter = ConvertToFilterOptions(subscription.Filter, heartbeat);
            }

            return req;
        }

        static Contracts.ReadReq.Types.Options.Types.AllOptions ConvertToAllOptions(LogPosition start) =>
            start switch {
                _ when start == LogPosition.Latest   => new() { End      = DefaultEmpty },
                _ when start == LogPosition.Earliest => new() { Start    = DefaultEmpty },
                _                                    => new() { Position = new() { CommitPosition = start, PreparePosition = start } }
            };

        static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(StreamName stream, StreamRevision start) =>
            start switch {
                _ when start == StreamRevision.Max => new() { StreamIdentifier = stream.Value, End      = DefaultEmpty },
                _ when start == StreamRevision.Min => new() { StreamIdentifier = stream.Value, Start    = DefaultEmpty },
                _                                  => new() { StreamIdentifier = stream.Value, Revision = (ulong)start.Value }
            };

        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter, HeartbeatOptions heartbeat) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
                StreamFilter f => new() { StreamIdentifier = new() { Regex = f.Expression } },
                SchemaFilter f => new() { EventType        = new() { Regex = f.Expression } },

                ExpressionFilter { ServerFilter: StreamFilter f } => new()  { StreamIdentifier = new() { Regex = f.Expression } },
                ExpressionFilter { ServerFilter: SchemaFilter f } => new () { EventType        = new() { Regex = f.Expression } },

                _ => null
            };

            if (options is null)
                return null;

            options.Max = (uint)heartbeat.RecordsThreshold;
            options.CheckpointIntervalMultiplier = 1;

            return options;
        }
    }

    public static CheckpointBarrier MapToBarrier(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
        var timestamp = checkpoint.Timestamp.ToDateTimeOffset();
        return new CheckpointBarrier(BarrierId.New(), CheckpointBarrierTrigger.Interval, timestamp);
    }

    public static CheckpointBarrier MapToBarrier(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
        var timestamp = caughtUp.Timestamp.ToDateTimeOffset();
        return new CheckpointBarrier(BarrierId.New(), CheckpointBarrierTrigger.CaughtUp, timestamp);
    }

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
        var revision  = StreamRevision.From((long)recordedEvent.StreamRevision);
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

        var recordPosition = RecordPosition.FromStream(stream, revision, position);

        var link = readEvent.Link is { } linkEvent
            ? new Link {
                Name     = linkEvent.Metadata[Constants.Metadata.Type],
                Position = (long)linkEvent.CommitPosition,
                Revision = (long)linkEvent.StreamRevision,
                Stream   = StreamName.From(linkEvent.StreamIdentifier!),
            }
            : Link.None;

        var indexRevision = readEvent.HasCommitPosition
            ? StreamRevision.From((long)readEvent.CommitPosition)
            : link.Revision == StreamRevision.Unset ? revision : link.Revision;

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

            // what? not sure...
            Link           = link,
            IndexRevision  = indexRevision
        };

        // now decode the record if required
        if (!skipDecoding)
            await record.TryDecode(ct).ConfigureAwait(false);

        return record;
    }
}
