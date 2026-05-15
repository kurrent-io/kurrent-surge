#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Diagnostics;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Client;
using KurrentDB.Protocol.V1;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

static class StreamsClientV1Mapper {
    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly Empty                                                     DefaultEmpty               = new();

    internal static class Requests {
        public static Contracts.DeleteReq CreateDeleteRequest(StreamName stream, Revision revision) {
            var req = new Contracts.DeleteReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        public static Contracts.TombstoneReq CreateTombstoneRequest(StreamName stream, Revision revision) {
            var req = new Contracts.TombstoneReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        public static Contracts.ReadReq CreateInspectRecordRequest(LogPosition position) {
            return new() {
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

        public static Contracts.ReadReq CreateReadStreamEdgeRequest(StreamName stream, ReadDirection direction, long? count = null) {
            return new() {
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
        
        public static Contracts.ReadReq CreateReadLastStreamEventRequest(StreamName stream, long? count = null) {
            return new() {
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
        
        public static Contracts.ReadReq CreateReadFirstStreamEventRequest(StreamName stream, long? count = null) {
            // if scavenged, the first event might not be the one with revision 0,
            // so we need to read from the start until we find the first non null event.
            return new() {
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
        public static Contracts.ReadReq CreateReadRecordsRequest(ReadRecordsRequest options) {
            var filter = options.Filter;

            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = options.Direction switch {
                    ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                    ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                },
                Count = (ulong)options.Limit
            }};

            // When the filter targets an index stream ($idx-*), read from $all
            // with a stream prefix filter — secondary indexes are consumed via
            // $all filtering, not by reading the index stream directly.
            // When it targets a regular stream, read from that stream directly.
            if (filter.IsStreamFilter && filter.TargetStream.IsIndexStream) {
                req.Options.All    = ConvertToAllOptions(options.Start);
                req.Options.Filter = new() {
                    StreamIdentifier        = new() { Prefix = { filter.TargetStream.Value } },
                    CheckpointIntervalMultiplier = 1,
                    Count                   = DefaultEmpty
                };
            }
            else if (filter.IsStreamFilter) {
                req.Options.Stream   = ConvertToStreamOptions(filter.TargetStream, Revision.Min);
                req.Options.NoFilter = DefaultEmpty;
            }
            else {
                req.Options.All = ConvertToAllOptions(options.Start);

                var filterOptions = ConvertToFilterOptions(filter);
                if (filterOptions is not null)
                    req.Options.Filter = filterOptions;
                else
                    req.Options.NoFilter = DefaultEmpty;
            }

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
    
    // public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
    //     var position  = LogPosition.From((long)checkpoint.CommitPosition);
    //     var timestamp = checkpoint.Timestamp.ToDateTimeOffset();
    //     return Heartbeat.CreateCheckpoint(position, timestamp);
    // }
    //
    // public static Heartbeat MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
    //     var position  = caughtUp.Position is not null ? LogPosition.From((long)caughtUp.Position.CommitPosition) : LogPosition.Unset;
    //     var revision  = caughtUp.HasStreamRevision ? Revision.From(caughtUp.StreamRevision) : Revision.Unset;
    //     var timestamp = caughtUp.Timestamp.ToDateTimeOffset();
    //     return Heartbeat.CreateCaughtUp(position, revision, timestamp);
    // }
    
    // public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) =>
    //     new(CheckpointId.New(), CheckpointTrigger.Interval, checkpoint.Timestamp.ToDateTimeOffset(), PartitionPositions.Empty);
    //
    // public static Checkpoint MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) =>
    //     new(CheckpointId.New(), CheckpointTrigger.CaughtUp, caughtUp.Timestamp.ToDateTimeOffset(), PartitionPositions.Empty);


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
            .Decode(recorded.CustomMetadata.Memory, new(stream, schemaName, schemaFormat))
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
