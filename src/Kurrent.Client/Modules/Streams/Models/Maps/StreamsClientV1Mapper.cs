#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Client;
using KurrentDB.Protocol.V1;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

// static class ProtocolMapping {
//     static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
//     static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
//     static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new(); // old was { Structured = new() }
//     static readonly Empty                                                     DefaultEmpty               = new();
//
//     internal static class Requests {
//         public static Contracts.ReadReq CreateLogSubscriptionRequest(IRecordFilter filter, LogPosition startFrom) {
//             Debug.Assert(!filter.IsStreamFilter, "Stream filters should not reach CreateLogSubscriptionRequest.");
//             Debug.Assert(startFrom != LogPosition.Unset, "Start position must be resolved before reaching the mapper.");
//
//             var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
//                 ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
//                 Subscription  = DefaultSubscriptionOptions,
//                 UuidOption    = DefaultUuidOptions,
//                 ControlOption = DefaultControlOption,
//                 All           = ConvertToAllOptions(startFrom),
//             }};
//
//             if (ConvertToFilterOptions(filter) is { } options)
//                 req.Options.Filter = options;
//             else
//                 req.Options.NoFilter = DefaultEmpty;
//
//             return req;
//         }
//
//         public static Contracts.ReadReq CreateStreamSubscriptionRequest(IRecordFilter filter, Revision startFrom) {
//             Debug.Assert(filter.IsStreamFilter, "Only stream filters should reach CreateStreamSubscriptionRequest.");
//             Debug.Assert(startFrom != Revision.Unset, "Start revision must be resolved before reaching the mapper.");
//
//             var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
//                 ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
//                 Subscription  = DefaultSubscriptionOptions,
//                 UuidOption    = DefaultUuidOptions,
//                 ControlOption = DefaultControlOption,
//                 Stream        = ConvertToStreamOptions(filter.TargetStream, startFrom),
//                 NoFilter      = DefaultEmpty
//             }};
//
//             return req;
//         }
//
//         static Contracts.ReadReq.Types.Options.Types.AllOptions ConvertToAllOptions(LogPosition start) {
//             Debug.Assert(start != LogPosition.Unset, "Start position must be resolved before reaching the mapper.");
//
//             return start switch {
//                 _ when start == LogPosition.Latest   => new() { End   = DefaultEmpty },
//                 _ when start == LogPosition.Earliest => new() { Start = DefaultEmpty },
//                 _ => new() {
//                     Position = new() {
//                         CommitPosition  = start,
//                         PreparePosition = start
//                     }
//                 }
//             };
//         }
//
//         static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(StreamName stream, Revision start) {
//             Debug.Assert(stream != StreamName.Unset, "Stream name must be resolved before reaching the mapper.");
//             Debug.Assert(start != Revision.Unset, "Start revision must be resolved before reaching the mapper.");
//
//             return start switch {
//                 _ when start == Revision.Max => new() { StreamIdentifier = stream.Value, End      = DefaultEmpty },
//                 _ when start == Revision.Min => new() { StreamIdentifier = stream.Value, Start    = DefaultEmpty },
//                 _                            => new() { StreamIdentifier = stream.Value, Revision = (ulong)start.Value }
//             };
//         }
//
//         static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter) {
//             Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
//                 StreamFilter f => new() { StreamIdentifier = new() { Regex = f.Expression } },
//
//                 ExpressionFilter { ServerFilter: StreamFilter f } => new() { StreamIdentifier = new() { Regex = f.Expression } },
//
//                 _ => null
//             };
//
//             if (options is null) return null;
//
//             options.Max = uint.MaxValue; // delay as much as we can - will fix on v2
//
//             options.CheckpointIntervalMultiplier = 1;
//
//             return options;
//         }
//     }
//
//     public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) =>
//         new("", CheckpointTrigger.Interval, checkpoint.Timestamp.ToDateTimeOffset());
//
//     public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) =>
//         new("", CheckpointTrigger.CaughtUp, caughtUp.Timestamp.ToDateTimeOffset());
// }


static class StreamsClientV1Mapper {
    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly Empty                                                     DefaultEmpty               = new();

    internal static class Requests {
        /// <summary>
        /// Creates a request to delete a stream at the specified revision.
        /// </summary>
        public static Contracts.DeleteReq CreateDeleteRequest(StreamName stream, Revision revision) {
            var req = new Contracts.DeleteReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        /// <summary>
        /// Creates a request to tombstone a stream at the specified revision.
        /// </summary>
        public static Contracts.TombstoneReq CreateTombstoneRequest(StreamName stream, Revision revision) {
            var req = new Contracts.TombstoneReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        /// <summary>
        ///  Creates a request to read a single record from the log at the specified position, resolving links if necessary.
        /// </summary>
        public static Contracts.ReadReq CreateInspectRecordRequest(LogPosition position) {
            return new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = 1,
                    ResolveLinks  = true,
                    All           = ConvertToAllOptions(position),
                    ReadDirection = position == LogPosition.Latest
                        ? Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards
                        : Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                }
            };
        }

        /// <summary>
        /// Creates a request to read the edge record from a stream in the specified direction.
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamEdgeRequest(StreamName stream, ReadDirection direction, long? count = null) {
            return new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = direction switch {
                        ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                        ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                    },
                    Stream = ConvertToStreamOptions(
                        stream, start: direction switch {
                            ReadDirection.Backwards => Revision.Max,
                            ReadDirection.Forwards  => Revision.Min,
                        }
                    ),
                }
            };
        }

        /// <summary>
        /// Creates a request to read the last event in the specified stream.
        /// </summary>
        public static Contracts.ReadReq CreateReadLastStreamEventRequest(StreamName stream, long? count = null) {
            return new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                    Stream        = ConvertToStreamOptions(stream, Revision.Max),
                }
            };
        }

        /// <summary>
        /// Creates a request to read the first event in the specified stream.
        /// </summary>
        /// <remarks>
        /// if scavenged, the first event might not be the one with revision 0,
        /// so we need to read from the start until we find the first non null event.
        /// </remarks>
        public static Contracts.ReadReq CreateReadFirstStreamEventRequest(StreamName stream, long? count = null) {
            return new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                    Stream        = ConvertToStreamOptions(stream, Revision.Min),
                }
            };
        }

        /// <summary>
        /// Creates a request to read log records based on the provided options.
        /// </summary>
        public static Contracts.ReadReq CreateReadLogRequest(ReadRecordsRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = options.Direction switch {
                    ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                    ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                },
                All   = ConvertToAllOptions(options.Start),
                Count = (ulong)options.Limit
            }};

            var filterOptions = ConvertToFilterOptions(options.Filter);
            if (filterOptions is not null)
                req.Options.Filter = filterOptions;
            else
                req.Options.NoFilter = DefaultEmpty;

            return req;
        }

        /// <summary>
        /// Creates a request to read records from a specific stream based on the provided options.
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamRequest(ReadStreamRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = options.Direction switch {
                    ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                    ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                },
                Stream       = ConvertToStreamOptions(options.Stream, options.Start),
                Count        = (ulong)options.Limit,
                ResolveLinks = true
            }};

            var filterOptions = ConvertToFilterOptions(options.Filter);
            if (filterOptions is not null)
                req.Options.Filter = filterOptions;
            else
                req.Options.NoFilter = DefaultEmpty;

            return req;
        }

        // /// <summary>
        // /// Creates a request to subscribe to records based on the provided processing options.
        // /// </summary>
        // public static Contracts.ReadReq CreateSubscriptionRequest(ProcessorSettings options) {
        //     var req = new Contracts.ReadReq { Options = new Contracts.ReadReq.Types.Options {
        //         Subscription  = DefaultSubscriptionOptions,
        //         UuidOption    = DefaultUuidOptions,
        //         ControlOption = DefaultControlOption,
        //         ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards
        //     }};
        //
        //     var subscription = options.Subscription;
        //     var heartbeat    = new HeartbeatOptions(true, options.Checkpoint.Threshold);
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
        //         0 => Revision.Min,
        //         _ => Revision.From(subscription.StartPosition)
        //     };
        //
        //     if (subscription.Filter == RecordFilter.None)
        //         req.Options.NoFilter = DefaultEmpty;
        //     else if (subscription.Filter.TargetStream != StreamName.Unset && subscription.Filter.TargetStream != StreamName.All) {
        //         req.Options.Stream   = ConvertToStreamOptions(subscription.Filter.TargetStream, streamRevision);
        //         req.Options.NoFilter = DefaultEmpty;
        //     }
        //     else {
        //         req.Options.All    = ConvertToAllOptions(logPosition);
        //         req.Options.Filter = ConvertToFilterOptions(subscription.Filter, heartbeat);
        //     }
        //
        //     return req;
        // }

        static Contracts.ReadReq.Types.Options.Types.AllOptions ConvertToAllOptions(LogPosition start) =>
            start switch {
                _ when start == LogPosition.Latest   => new() { End      = DefaultEmpty },
                _ when start == LogPosition.Earliest => new() { Start    = DefaultEmpty },
                _                                    => new() { Position = new() { CommitPosition = start, PreparePosition = start } }
            };

        static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(StreamName stream, Revision start) =>
            start switch {
                _ when start == Revision.Max => new() { StreamIdentifier = stream.Value, End      = DefaultEmpty },
                _ when start == Revision.Min => new() { StreamIdentifier = stream.Value, Start    = DefaultEmpty },
                _                            => new() { StreamIdentifier = stream.Value, Revision = (ulong)start.Value }
            };

        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
                StreamFilter f => new() { StreamIdentifier = new() { Regex = f.Expression } },

                ExpressionFilter { ServerFilter: StreamFilter f } => new() { StreamIdentifier = new() { Regex = f.Expression } },

                _ => null
            };

            if (options is null)
                return null;

            options.CheckpointIntervalMultiplier = 1;
            options.Count = DefaultEmpty;

            return options;
        }
    }

    public static Heartbeat MapToHeartbeat(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
        var position  = LogPosition.From((long)checkpoint.CommitPosition);
        var timestamp = checkpoint.Timestamp.ToDateTimeOffset();
        return Heartbeat.CreateCheckpoint(position, timestamp);
    }

    public static Heartbeat MapToHeartbeat(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
        var position  = caughtUp.Position is not null ? LogPosition.From((long)caughtUp.Position.CommitPosition) : LogPosition.Unset;
        var revision  = caughtUp.HasStreamRevision ? Revision.From(caughtUp.StreamRevision) : Revision.Unset;
        var timestamp = caughtUp.Timestamp.ToDateTimeOffset();
        return Heartbeat.CreateCaughtUp(position, revision, timestamp);
    }

    public static LogPosition MapToLogPosition(this Contracts.DeleteResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };

    public static LogPosition MapToLogPosition(this Contracts.TombstoneResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };

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

        var link = readEvent.Link is { } linkEvent
            ? new Link {
                Name     = linkEvent.Metadata[Constants.Metadata.Type],
                Position = (long)linkEvent.CommitPosition,
                Revision = (long)linkEvent.StreamRevision,
                Stream   = StreamName.From(linkEvent.StreamIdentifier!),
            }
            : Link.Unset;

        // var indexRevision = readEvent.HasCommitPosition
        //     ? Revision.From((long)readEvent.CommitPosition)
        //     : link.Revision == Revision.Unset ? revision : link.Revision;

        // create a decoder
        IRecordDecoder decoder = new RecordDecoder(serializerProvider, registryPolicy);

        // and we are done
        var record = new Record(decoder) {
            Id             = recordId,
            // Stream         = stream,
            // Revision = revision,
            Position  = RecordPosition.From(position, stream, revision),
            Sequence  = sequenceId,
            Timestamp = timestamp,
            Data      = data,
            Metadata  = metadata,
            Schema    = schema,
            Link      = link

            // // what? not sure...
            // IndexRevision  = indexRevision
        };

        // now decode the record if required
        if (!skipDecoding)
            await record.TryDecode(ct).ConfigureAwait(false);

        return record;
    }
}
