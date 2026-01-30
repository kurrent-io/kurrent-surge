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
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new(); // old was { Structured = new() }
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

            if (options.Subscription.Filter == RecordFilter.None)
                req.Options.NoFilter = DefaultEmpty;
            else if (options.Subscription.Filter.TargetStream == StreamName.All) {
                req.Options.All    = ConvertToAllOptions(options.Subscription);
                req.Options.Filter = ConvertToFilterOptions(options.Subscription.Filter);
            }
            else if (options.Subscription.Filter.TargetStream != StreamName.None) {
                req.Options.Stream   = ConvertToStreamOptions(options.Subscription);
                req.Options.NoFilter = DefaultEmpty;
            }

            return req;
        }

        // /// <summary>
        // /// Creates a request to subscribe to records based on the provided processing options.
        // /// </summary>
        // public static Contracts.ReadReq CreateSubscriptionRequestj(ProcessorSettings options) { //old
        //     var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
        //         Subscription  = DefaultSubscriptionOptions,
        //         UuidOption    = DefaultUuidOptions,
        //         // ControlOption = DefaultControlOption,
        //         ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards
        //     }};
        //
        //     var subscription = options.Subscription;
        //
        //     // Convert StartPosition (long) to the appropriate position type
        //     var logPosition = subscription.StartPosition switch {
        //         0 when subscription.InitialPosition == SubscriptionInitialPosition.Earliest => LogPosition.Earliest,
        //         0 when subscription.InitialPosition == SubscriptionInitialPosition.Latest   => LogPosition.Latest,
        //         0                                                                           => LogPosition.Earliest,
        //         _                                                                           => LogPosition.From(subscription.StartPosition)
        //     };
        //
        //     var streamRevision = subscription.StartPosition switch {
        //         0 => StreamRevision.Min,
        //         _ => StreamRevision.From(subscription.StartPosition)
        //     };
        //
        //     if (subscription.Filter == RecordFilter.None)
        //         req.Options.NoFilter = DefaultEmpty;
        //     else if (subscription.Filter.TargetStream != StreamName.None && subscription.Filter.TargetStream != StreamName.All) {
        //         req.Options.Stream   = ConvertToStreamOptions(subscription.Filter.TargetStream, streamRevision);
        //         req.Options.NoFilter = DefaultEmpty;
        //     }
        //     else {
        //         req.Options.All    = ConvertToAllOptions(logPosition);
        //         req.Options.Filter = ConvertToFilterOptions(subscription.Filter);
        //     }
        //
        //     return req;
        // }
        //
        // public static Contracts.ReadReq CreateSubscriptionRequest2(ProcessorSettings options) {
        //     var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
        //         Subscription  = DefaultSubscriptionOptions,
        //         UuidOption    = DefaultUuidOptions,
        //         ControlOption = DefaultControlOption,
        //         ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards
        //     }};
        //
        //      var subOptions = options.Subscription;
        //
        //     if (subOptions.Filter == RecordFilter.None)
        //         req.Options.NoFilter = DefaultEmpty;
        //     else if (subOptions.Filter.TargetStream != StreamName.None && subOptions.Filter.TargetStream != StreamName.All)
        //         req.Options.Stream = ConvertToStreamOptions(subOptions.Filter.TargetStream, subOptions.StartPosition);
        //     else {
        //         req.Options.All    = ConvertToAllOptions(subOptions.StartPosition);
        //         req.Options.Filter = ConvertToFilterOptions(subOptions.Filter);
        //     }
        //
        //     return req;
        // }

        // /// <summary>
        // /// Creates a request to subscribe to records based on the provided processing options.
        // /// </summary>
        // public static Contracts.ReadReq CreateSubscriptionRequest9(ProcessorSettings options) {
        //     var subscriptionOptions = options.Subscription;
        //
        //
        //
        //     // Determine if reading from a specific stream or $all
        //     var isSpecificStream = subscriptionOptions.Filter.TargetStream != StreamName.None
        //                         && subscriptionOptions.Filter.TargetStream != StreamName.All;
        //
        //     var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
        //         //Subscription  = DefaultSubscriptionOptions,
        //         UuidOption    = DefaultUuidOptions,
        //         ControlOption = DefaultControlOption,
        //         ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards
        //     }};
        //
        //     // Only set Subscription when reading from $all, not when reading from a specific stream
        //     if (!isSpecificStream)
        //         req.Options.Subscription = DefaultSubscriptionOptions;
        //
        //     if (subscriptionOptions.Filter == RecordFilter.None)
        //         req.Options.NoFilter = DefaultEmpty;
        //     else if (isSpecificStream) {
        //         var streamRevision = subscriptionOptions.StartPosition switch {
        //             0 => StreamRevision.Min,
        //             _ => StreamRevision.From(subscriptionOptions.StartPosition)
        //         };
        //
        //         req.Options.Stream   = ConvertToStreamOptions(subscriptionOptions.Filter.TargetStream, streamRevision);
        //         req.Options.NoFilter = DefaultEmpty; // When reading from a specific stream, no additional filtering is needed
        //     }
        //     else {
        //         // Convert StartPosition (long) to the appropriate position type
        //         var logPosition = subscriptionOptions.StartPosition switch {
        //             0 when subscriptionOptions.InitialPosition == SubscriptionInitialPosition.Earliest => LogPosition.Earliest,
        //             0 when subscriptionOptions.InitialPosition == SubscriptionInitialPosition.Latest   => LogPosition.Latest,
        //             0                                                                                  => LogPosition.Earliest,
        //             _                                                                                  => LogPosition.From(subscriptionOptions.StartPosition)
        //         };
        //
        //         req.Options.All    = ConvertToAllOptions(logPosition);
        //         req.Options.Filter = ConvertToFilterOptions(subscriptionOptions.Filter);
        //     }
        //
        //     return req;
        // }

        static Contracts.ReadReq.Types.Options.Types.AllOptions ConvertToAllOptions(SubscriptionSettings options) {
            return options.InitialPosition switch {
                SubscriptionInitialPosition.Earliest => new() { Start = DefaultEmpty },
                SubscriptionInitialPosition.Latest   => new() { End   = DefaultEmpty },
                _                                    => new() { Start = DefaultEmpty }
            };
        }

        static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(SubscriptionSettings options) {
            return options.InitialPosition switch {
                SubscriptionInitialPosition.Earliest => new() { StreamIdentifier = options.Filter.TargetStream.Value, Start = DefaultEmpty },
                SubscriptionInitialPosition.Latest   => new() { StreamIdentifier = options.Filter.TargetStream.Value, End   = DefaultEmpty },
                _                                    => new() { StreamIdentifier = options.Filter.TargetStream.Value, Start = DefaultEmpty }
            };
        }

        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
                StreamFilter f => new() { StreamIdentifier = new() { Regex = f.Expression } },
                SchemaFilter f => new() { EventType        = new() { Regex = f.Expression } },

                ExpressionFilter { ServerFilter: StreamFilter f } => new()  { StreamIdentifier = new() { Regex = f.Expression } },
                ExpressionFilter { ServerFilter: SchemaFilter f } => new () { EventType        = new() { Regex = f.Expression } },

                _ => null
            };

            if (options is null)
                return null;

            options.Max                          = uint.MaxValue; // delay as much as we can - will fix on v2
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
