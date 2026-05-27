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
    /// <summary>
    /// Default number of raw log records the server scans per filter page before returning results.
    /// Tuned for high-throughput streaming — large enough to minimize round trips on sparse filters,
    /// bounded enough to avoid unbounded server work per page.
    /// </summary>
    internal const uint DefaultMaxSearchWindow = 4096;

    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly Empty                                                     DefaultEmpty               = new();

    #region ->> Request Builders <<-

    internal static class Requests {
        /// <summary>
        /// Builds a soft-delete request. When <paramref name="revision"/> is <see cref="Revision.Unset"/>,
        /// sends <c>StreamExists</c> (any-revision delete); otherwise sends the expected revision for
        /// optimistic concurrency.
        /// </summary>
        public static Contracts.DeleteReq CreateDeleteRequest(StreamName stream, Revision revision) {
            var req = new Contracts.DeleteReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        /// <summary>
        /// Builds a hard-delete (tombstone) request. Same concurrency semantics as
        /// <see cref="CreateDeleteRequest"/> — the stream name becomes permanently unusable.
        /// </summary>
        public static Contracts.TombstoneReq CreateTombstoneRequest(StreamName stream, Revision revision) {
            var req = new Contracts.TombstoneReq { Options = new() { StreamIdentifier = stream.Value } };
            return revision == Revision.Unset
                ? req.With(x => x.Options.StreamExists = DefaultEmpty)
                : req.With(x => x.Options.Revision = (ulong)revision.Value);
        }

        /// <summary>
        /// Builds a single-record read at a specific log position with link resolution enabled.
        /// Used for inspecting an individual record by its commit position.
        /// Reads backwards from <see cref="LogPosition.Latest"/>, forwards otherwise.
        /// </summary>
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

        /// <summary>
        /// Builds a read from the edge of a stream — head (backwards) or tail (forwards).
        /// Used for stream info probes like finding the last or first event.
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamEdgeRequest(StreamName stream, ReadDirection direction, long? count = null) {
            return new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = ToReadDirection(direction),
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
        /// Reads backwards from the end of a stream. Convenience shortcut for
        /// <see cref="CreateReadStreamEdgeRequest"/> with <see cref="ReadDirection.Backwards"/>.
        /// </summary>
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

        /// <summary>
        /// Reads forwards from the start of a stream. After a scavenge the first surviving event
        /// may not be at revision 0, so callers should read until the first non-null event.
        /// </summary>
        public static Contracts.ReadReq CreateReadFirstStreamEventRequest(StreamName stream, long? count = null) {
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
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = (ulong)options.Limit,
                ResolveLinks  = options.ResolveLinks,
            }};

            // Stream branch uses Revision.Min — read whole stream from beginning when
            // the filter resolves to a StreamNameFilter. Log branch uses options.Start.
            var start = RecordPosition.From(options.Start, StreamName.Unset, Revision.Min);
            RouteByFilter(req.Options, options.Filter, start);

            return req;
        }

        /// <summary>
        /// Creates a request to read records from a specific stream based on the provided options.
        /// Filter is source-of-truth for routing: if no filter is supplied, a <see cref="StreamNameFilter"/>
        /// is synthesized from <see cref="ReadStreamRequest.Stream"/>. Otherwise the caller's filter wins
        /// (the validator ensures any explicit <see cref="StreamNameFilter"/> targets the same stream).
        /// </summary>
        public static Contracts.ReadReq CreateReadStreamRequest(ReadStreamRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Count         = (ulong)options.Limit,
                ResolveLinks  = options.ResolveLinks,
            }};

            // Empty filter → synthesize a StreamNameFilter from the explicit stream property.
            // Any other filter wins outright.
            var filter = options.Filter is EmptyFilter
                ? StreamNameFilter.Parse(options.Stream)
                : options.Filter;

            // Stream branch uses options.Start (Revision). LogPosition.Earliest is a safe
            // placeholder for the $all branch — never reached when filter is StreamNameFilter.
            var start = RecordPosition.From(LogPosition.Earliest, options.Stream, options.Start);
            RouteByFilter(req.Options, filter, start);

            return req;
        }

        /// <summary>
        /// Builds a subscription request. <see cref="RouteByFilter"/> dispatches between a
        /// single-stream subscription and a filtered <c>$all</c> subscription based on the
        /// unwrapped filter. For filtered <c>$all</c>, the server emits periodic
        /// <see cref="IntervalCheckpoint"/> barriers paced by <paramref name="maxSearchWindow"/>.
        /// </summary>
        public static Contracts.ReadReq CreateSubscriptionRequest(
            RecordFilter filter,
            RecordPosition start,
            bool resolveLinks,
            uint maxSearchWindow = DefaultMaxSearchWindow
        ) {
            var req = new Contracts.ReadReq { Options = new() {
                ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                Subscription  = DefaultSubscriptionOptions,
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ResolveLinks  = resolveLinks,
            }};

            RouteByFilter(req.Options, filter, start, maxSearchWindow);
            return req;
        }
        
        /// <summary>
        /// Applies the read/subscribe wire shape to a V1 Options block. Unwraps
        /// <see cref="ExpressionFilter"/>, routes between <c>Options.Stream</c> and
        /// <c>Options.All</c>, and sets the appropriate server-side filter (or
        /// <c>NoFilter</c>). Self-contained: owns both the unwrap and the
        /// <c>FilterOptions</c> dispatch so no caller can bypass either.
        /// </summary>
        /// <remarks>
        /// Decision table (after unwrap):
        /// <code>
        ///   Server filter type    →  Wire shape
        ///   ──────────────────── ── ──────────────────────────────────────────────────
        ///   StreamNameFilter        Options.Stream(target, start.Revision) + NoFilter
        ///   IndexFilter             Options.All(start.LogPosition) + Filter.Prefix(IndexName)
        ///   StreamRegexFilter       Options.All(start.LogPosition) + Filter.Regex(Expression)
        ///   EmptyFilter / other     Options.All(start.LogPosition) + NoFilter
        /// </code>
        /// </remarks>
        /// <param name="opts">Options block to mutate.</param>
        /// <param name="filter">The record filter (may be an <see cref="ExpressionFilter"/> wrapping a server filter).</param>
        /// <param name="start">Start position. <see cref="RecordPosition.LogPosition"/> is used when routing
        /// to <c>$all</c>; <see cref="RecordPosition.Revision"/> is used when routing to a single stream.</param>
        /// <param name="maxSearchWindow">Server scan budget per filter page. Applied only when a server-side filter is set.</param>
        static void RouteByFilter(
            Contracts.ReadReq.Types.Options opts,
            RecordFilter filter,
            RecordPosition start,
            uint maxSearchWindow = DefaultMaxSearchWindow
        ) {
            var serverFilter = filter.UnwrapFilter();

            // Direct stream read — explicit name overrides $all scan.
            if (serverFilter is StreamNameFilter snf) {
                opts.Stream   = ConvertToStreamOptions(snf.TargetStream, start.Revision);
                opts.NoFilter = DefaultEmpty;
                return;
            }

            // All other filters target the log.
            opts.All = ConvertToAllOptions(start.LogPosition);

            if (ConvertToFilterOptions(serverFilter, maxSearchWindow) is { } filterOptions)
                opts.Filter = filterOptions;
            else
                opts.NoFilter = DefaultEmpty;

            // Dispatch unwrapped filter → V1 FilterOptions.
            //   IndexFilter        → StreamIdentifier.Prefix (server index dispatch, DuckDB path)
            //   StreamRegexFilter  → StreamIdentifier.Regex  (filtered $all scan)
            //   Anything else      → null (caller emits NoFilter)
            static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(
                RecordFilter f, uint maxSearchWindow
            ) {
                Contracts.ReadReq.Types.Options.Types.FilterOptions? options = f switch {
                    IndexFilter idx     => new() { StreamIdentifier = new() { Prefix = { idx.IndexName } } },
                    StreamRegexFilter r => new() { StreamIdentifier = new() { Regex = r.Expression } },
                    _                   => null
                };

                if (options is null)
                    return null;

                // FilterOptions.window is a oneof of `max` (uint32) or `count` (Empty → server default = 32).
                // We always use `max`: the server default is far too small for sparse filters (excessive
                // round trips on near-empty pages) and floods subscriptions with IntervalCheckpoints
                // (one every `multiplier × window` scanned records). Explicit `max` gives the SDK full
                // control independent of server-side defaults that may change between versions.
                options.Max = maxSearchWindow;
                // multiplier = 1 keeps the relationship simple: one IntervalCheckpoint per scan window.
                options.CheckpointIntervalMultiplier = 1;
                return options;
            }
        }
        
        /// <summary>Maps the SDK read direction to the V1 proto enum.</summary>
        static Contracts.ReadReq.Types.Options.Types.ReadDirection ToReadDirection(ReadDirection direction) =>
            direction switch {
                ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
            };

        /// <summary>
        /// Converts a <see cref="LogPosition"/> to the V1 AllOptions positioning.
        /// <see cref="LogPosition.Latest"/> maps to End (read backwards from head),
        /// <see cref="LogPosition.Earliest"/> maps to Start (read from beginning),
        /// any other value maps to an explicit commit/prepare position pair.
        /// </summary>
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

        /// <summary>
        /// Converts a stream name and revision to the V1 StreamOptions positioning.
        /// <see cref="Revision.Max"/> maps to End (read backwards from head),
        /// <see cref="Revision.Min"/> maps to Start (read from beginning),
        /// any other value maps to an explicit stream revision.
        /// </summary>
        static Contracts.ReadReq.Types.Options.Types.StreamOptions ConvertToStreamOptions(StreamName stream, Revision start) {
            Debug.Assert(stream != StreamName.Unset, "Stream name must be resolved before reaching the mapper.");
            Debug.Assert(start != Revision.Unset, "Start revision must be resolved before reaching the mapper.");

            return start switch {
                _ when start == Revision.Max => new() { StreamIdentifier = stream.Value, End      = DefaultEmpty },
                _ when start == Revision.Min => new() { StreamIdentifier = stream.Value, Start    = DefaultEmpty },
                _                            => new() { StreamIdentifier = stream.Value, Revision = (ulong)start.Value }
            };
        }

    }

    #endregion // Request Builders

    #region ->> Response Mappers <<-

    /// <summary>
    /// Maps a V1 <c>ReadResp.Checkpoint</c> to an <see cref="IntervalCheckpoint"/>.
    /// </summary>
    /// <remarks>
    /// Emitted periodically during filtered $all reads and subscriptions after the server has
    /// scanned <c>checkpointIntervalMultiplier × maxSearchWindow</c> raw log records, regardless
    /// of how many matched the filter. The position represents where in the log the server has
    /// scanned up to, allowing the client to resume without reprocessing filtered-out records.
    /// </remarks>
    public static IntervalCheckpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
        var position = LogPosition.From((long)checkpoint.CommitPosition);

        return new(CheckpointId.New(), checkpoint.Timestamp.ToDateTimeOffset()) {
            Positions = [RecordPosition.From(position)]
        };
    }

    /// <summary>
    /// Maps a V1 <c>ReadResp.CaughtUp</c> to a <see cref="CaughtUpCheckpoint"/>.
    /// </summary>
    /// <remarks>
    /// Emitted exactly once per subscription lifecycle when historical replay completes and the
    /// subscription transitions to live mode. Only fires if the subscription started behind
    /// the head of the stream/log. For $all subscriptions the position field is populated;
    /// for stream subscriptions the stream revision is populated instead. The fields are
    /// mutually exclusive by convention depending on the subscription target.
    /// </remarks>
    public static CaughtUpCheckpoint MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
        var position = caughtUp.Position is not null ? LogPosition.From((long)caughtUp.Position.CommitPosition) : LogPosition.Unset;
        var revision = caughtUp.HasStreamRevision ? Revision.From(caughtUp.StreamRevision) : Revision.Unset;

        return new(CheckpointId.New(), caughtUp.Timestamp.ToDateTimeOffset()) {
            Positions = [RecordPosition.From(position, StreamName.Unset, revision)]
        };
    }

    /// <summary>
    /// Maps a V1 <c>DeleteResp.Position</c> to a <see cref="LogPosition"/>.
    /// Returns <see cref="LogPosition.Unset"/> when the server omits the position
    /// (e.g. soft-delete of a stream that was already deleted).
    /// </summary>
    public static LogPosition MapToLogPosition(this Contracts.DeleteResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };

    /// <summary>
    /// Maps a V1 <c>TombstoneResp.Position</c> to a <see cref="LogPosition"/>.
    /// Returns <see cref="LogPosition.Unset"/> when the server omits the position.
    /// </summary>
    public static LogPosition MapToLogPosition(this Contracts.TombstoneResp.Types.Position? position) =>
        position switch {
            null => LogPosition.Unset,
            _    => LogPosition.From((long)position.CommitPosition)
        };

    /// <summary>
    /// Maps a V1 <c>ReadResp.ReadEvent</c> to a <see cref="Record"/>.
    /// </summary>
    /// <remarks>
    /// The server populates <c>event</c> and/or <c>link</c> in three combinations:
    /// event + link = resolved link (record is the target event, link carries source metadata);
    /// link only = unresolved link (target deleted or resolution disabled, record IS the link);
    /// event only = regular event. Schema info in V1 is encoded in the metadata map
    /// (<c>content-type</c> and <c>type</c> keys); in V2 schema is a first-class wire concept.
    /// </remarks>
    public static async ValueTask<Record> MapToRecordAsync(
        this Contracts.ReadResp.Types.ReadEvent readEvent,
        SequenceId sequenceId,
        ISchemaSerializerProvider serializerProvider,
        IMetadataDecoder metadataDecoder,
        SchemaRegistryPolicy registryPolicy,
        bool skipDecoding = false,
        CancellationToken ct = default
    ) {
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

        // V1 encodes schema info in the event metadata map, not as a first-class wire concept.
        // content-type → SchemaDataFormat (always json or octet-stream)
        // type → SchemaName (the event type name)
        var schemaFormat = recorded.Metadata[Constants.Metadata.ContentType].GetSchemaDataFormat();
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

    #endregion
}
