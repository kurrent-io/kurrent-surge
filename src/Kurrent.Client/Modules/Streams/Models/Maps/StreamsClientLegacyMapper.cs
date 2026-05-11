#pragma warning disable CS8524 // non-exhaustive switch over enum
#pragma warning disable CS8509 // non-exhaustive switch

using KurrentDB.Protocol.V1;
using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

/// <summary>
/// Single source of truth for the legacy (V1 gRPC) Streams protocol mapping.
/// Replaces the two drifted copies:
///   - src/Kurrent.Client/Modules/Streams/Models/Maps/StreamsClientV1Mapper.cs
///   - src/Kurrent.Processors/Models/StreamsClientV1Mapper.cs
///
/// Both one-shot reads (Client) and live subscriptions (Processors) route through
/// <see cref="Requests.ApplyLogReadShape"/>, which encodes every KurrentDB server
/// rule for filter/stream/index dispatch in ONE place.
/// </summary>
/// <remarks>
/// <para><b>Index dispatch rules (from KurrentDB Streams.Read.cs):</b></para>
/// <list type="number">
///   <item><description>
///     <b>Index-targeted filter (<c>$idx-*</c>)</b> → <c>Options.All</c> + <c>Filter.StreamIdentifier.Prefix</c>
///     with exactly one entry, no <c>Regex</c>. Any other shape silently falls off the server's
///     index dispatch into a regular filtered <c>$all</c> scan (or returns <c>InvalidArgument</c>).
///   </description></item>
///   <item><description>
///     <b>Regular stream target</b> → <c>Options.Stream</c> (name + revision), <c>NoFilter</c>.
///     Reading an <c>$idx-*</c> name this way returns <c>StreamNotFound</c> — it bypasses index dispatch.
///   </description></item>
///   <item><description>
///     <b>Non-targeted filter (regex / expression)</b> → <c>Options.All</c> + <c>Filter.StreamIdentifier.Regex</c>.
///   </description></item>
///   <item><description>
///     <b>No filter</b> → <c>Options.All</c> + <c>NoFilter</c>.
///   </description></item>
/// </list>
/// <para><b>Fields ignored on the index path:</b> <c>Filter.Max</c>, <c>Filter.CheckpointIntervalMultiplier</c>.
/// They are still set to satisfy the proto oneof contracts, but the server does not read them.</para>
/// </remarks>
static class StreamsClientLegacyMapper {
    #region ->> Defaults <<-

    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly Empty                                                     DefaultEmpty               = new();

    #endregion

    #region ->> Requests <<-

    internal static class Requests {
        #region ->> Write-path <<-

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

        #endregion

        #region ->> Direct stream reads <<-

        public static Contracts.ReadReq CreateReadStreamEdgeRequest(StreamName stream, ReadDirection direction, long? count = null) =>
            new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = ToReadDirection(direction),
                    Stream        = ConvertToStreamOptions(
                        stream,
                        start: direction switch {
                            ReadDirection.Backwards => Revision.Max,
                            ReadDirection.Forwards  => Revision.Min,
                        }
                    ),
                }
            };

        public static Contracts.ReadReq CreateReadLastStreamEventRequest(StreamName stream, long? count = null) =>
            new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                    Stream        = ConvertToStreamOptions(stream, Revision.Max),
                }
            };

        /// <summary>
        /// Reads from the start of a stream. If scavenged, the first event may not be at revision 0,
        /// so the caller iterates until the first non-null event.
        /// </summary>
        public static Contracts.ReadReq CreateReadFirstStreamEventRequest(StreamName stream, long? count = null) =>
            new() {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    NoFilter      = DefaultEmpty,
                    Count         = count is null ? ulong.MaxValue : (ulong)count,
                    ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                    Stream        = ConvertToStreamOptions(stream, Revision.Min),
                }
            };

        public static Contracts.ReadReq CreateReadStreamRequest(ReadStreamRequest options) {
            var req = new Contracts.ReadReq { Options = new() {
                UuidOption    = DefaultUuidOptions,
                ControlOption = DefaultControlOption,
                ReadDirection = ToReadDirection(options.Direction),
                Stream        = ConvertToStreamOptions(options.Stream, options.Start),
                Count         = (ulong)options.Limit,
                ResolveLinks  = true
            }};

            var filter = ConvertToFilterOptions(options.Filter);
            if (filter is not null)
                req.Options.Filter = filter;
            else
                req.Options.NoFilter = DefaultEmpty;

            return req;
        }

        #endregion

        #region ->> Log reads & subscriptions <<-

        public static Contracts.ReadReq CreateInspectRecordRequest(LogPosition position) =>
            new() {
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

        /// <summary>
        /// One-shot filtered read over the log. Routes index-targeted filters through
        /// <c>$all + Filter.Prefix</c>; routes regular stream targets through direct stream reads;
        /// routes everything else through <c>$all</c> with or without a regex filter.
        /// </summary>
        public static Contracts.ReadReq CreateReadRecordsRequest(ReadRecordsRequest options) {
            var req = new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    ReadDirection = ToReadDirection(options.Direction),
                    Count         = (ulong)options.Limit,
                }
            };

            ApplyLogReadShape(req.Options, options.Filter, logStart: options.Start, streamStart: Revision.Min);
            return req;
        }

        /// <summary>
        /// Live forwards-only subscription over the log. Shares the same routing decisions as
        /// <see cref="CreateReadRecordsRequest"/>, so index-targeted subscriptions correctly
        /// hit the server's index dispatch path instead of attempting to read
        /// <c>$idx-*</c> as a regular stream (the old Processors bug).
        /// </summary>
        public static Contracts.ReadReq CreateSubscriptionRequest(IRecordFilter filter, RecordPosition start) {
            var req = new Contracts.ReadReq {
                Options = new() {
                    UuidOption    = DefaultUuidOptions,
                    ControlOption = DefaultControlOption,
                    Subscription  = DefaultSubscriptionOptions,
                    ReadDirection = Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
                }
            };

            ApplyLogReadShape(req.Options, filter, logStart: start.LogPosition, streamStart: start.Revision);
            return req;
        }

        #endregion

        #region ->> Core routing <<-

        /// <summary>
        /// Applies the correct <c>Options.All</c>/<c>Options.Stream</c> + filter combination
        /// to a read or subscribe request. This is the ONE place that knows how to route
        /// filters on the V1 wire — used by both one-shot reads and subscriptions.
        /// </summary>
        /// <remarks>
        /// Decision table:
        /// <code>
        ///   IsStreamFilter  TargetStream     →  Wire shape
        ///   ─────────────── ──────────────── ── ────────────────────────────────────────
        ///   true            $idx-*              Options.All + Filter.Prefix = [target]
        ///   true            &lt;regular&gt;     Options.Stream(target, streamStart)
        ///   false           (n/a)               Options.All + Filter.Regex (if derivable)
        ///   false           (n/a)               Options.All + NoFilter       (otherwise)
        /// </code>
        /// </remarks>
        static void ApplyLogReadShape(
            Contracts.ReadReq.Types.Options opts,
            IRecordFilter filter,
            LogPosition logStart,
            Revision streamStart
        ) {
            // 1. Index-targeted → $all + Prefix (single entry, no Regex)
            if (filter.IsStreamFilter && filter.TargetStream.IsIndexStream) {
                opts.All    = ConvertToAllOptions(logStart);
                opts.Filter = new() {
                    StreamIdentifier             = new() { Prefix = { filter.TargetStream.Value } },
                    CheckpointIntervalMultiplier = 1,    // ignored on index path, required by proto
                    Count                        = DefaultEmpty,
                };
                return;
            }

            // 2. Regular stream target → direct stream read
            if (filter.IsStreamFilter) {
                opts.Stream   = ConvertToStreamOptions(filter.TargetStream, streamStart);
                opts.NoFilter = DefaultEmpty;
                return;
            }

            // 3 + 4. Non-targeted filter on $all, or no filter
            opts.All = ConvertToAllOptions(logStart);

            var filterOptions = ConvertToFilterOptions(filter);
            if (filterOptions is not null)
                opts.Filter = filterOptions;
            else
                opts.NoFilter = DefaultEmpty;
        }

        #endregion

        #region ->> Helpers <<-

        static Contracts.ReadReq.Types.Options.Types.ReadDirection ToReadDirection(ReadDirection direction) =>
            direction switch {
                ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
                ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
            };

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

        /// <summary>
        /// Emits a regex-based filter for non-targeted filters only. Never use this for
        /// <c>$idx-*</c> reads — the server's index dispatch ignores regex filters.
        /// </summary>
        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(IRecordFilter filter) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = filter switch {
                StreamFilter f                                   => new() { StreamIdentifier = new() { Regex = f.Expression } },
                ExpressionFilter { ServerFilter: StreamFilter f} => new() { StreamIdentifier = new() { Regex = f.Expression } },
                _                                                => null
            };

            if (options is null)
                return null;

            options.CheckpointIntervalMultiplier = 1;
            options.Count                        = DefaultEmpty;
            return options;
        }

        #endregion
    }

    #endregion
    
    
    //
    // #region ->> Response mapping <<-
    //
    // public static Heartbeat MapToHeartbeat(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
    //     var position  = LogPosition.From((long)checkpoint.CommitPosition);
    //     var timestamp = checkpoint.Timestamp.ToDateTimeOffset();
    //     return Heartbeat.CreateCheckpoint(position, timestamp);
    // }
    //
    // public static Heartbeat MapToHeartbeat(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
    //     var position  = caughtUp.Position is not null ? LogPosition.From((long)caughtUp.Position.CommitPosition) : LogPosition.Unset;
    //     var revision  = caughtUp.HasStreamRevision ? Revision.From(caughtUp.StreamRevision) : Revision.Unset;
    //     var timestamp = caughtUp.Timestamp.ToDateTimeOffset();
    //     return Heartbeat.CreateCaughtUp(position, revision, timestamp);
    // }
    //
    // public static LogPosition MapToLogPosition(this Contracts.DeleteResp.Types.Position? position) =>
    //     position switch {
    //         null => LogPosition.Unset,
    //         _    => LogPosition.From((long)position.CommitPosition)
    //     };
    //
    // public static LogPosition MapToLogPosition(this Contracts.TombstoneResp.Types.Position? position) =>
    //     position switch {
    //         null => LogPosition.Unset,
    //         _    => LogPosition.From((long)position.CommitPosition)
    //     };
    //
    // public static async ValueTask<Record> MapToRecordAsync(
    //     this Contracts.ReadResp.Types.ReadEvent readEvent,
    //     SequenceId sequenceId,
    //     ISchemaSerializerProvider serializerProvider,
    //     IMetadataDecoder metadataDecoder,
    //     SchemaRegistryPolicy registryPolicy,
    //     bool skipDecoding = false,
    //     CancellationToken ct = default
    // ) {
    //     var recordedEvent = readEvent.Event;
    //
    //     var stream    = StreamName.From(recordedEvent.StreamIdentifier);
    //     var recordId  = Uuid.FromDto(recordedEvent.Id).ToGuid();
    //     var revision  = Revision.From((long)recordedEvent.StreamRevision);
    //     var position  = LogPosition.From((long)recordedEvent.CommitPosition);
    //     var data      = recordedEvent.Data.Memory;
    //     var timestamp = Convert.ToInt64(recordedEvent.Metadata[Constants.Metadata.Created]).FromTicksSinceEpoch();
    //
    //     var schemaFormat = recordedEvent.Metadata[Constants.Metadata.ContentType].GetSchemaDataFormat();
    //     var schemaName   = SchemaName.From(recordedEvent.Metadata[Constants.Metadata.Type]);
    //
    //     var metadata = await metadataDecoder
    //         .Decode(recordedEvent.CustomMetadata.Memory, new(stream, schemaName, schemaFormat))
    //         .ConfigureAwait(false);
    //
    //     var schema = metadata.ExtractSchemaInfo();
    //
    //     var link = readEvent.Link is { } linkEvent
    //         ? new() {
    //             Name     = linkEvent.Metadata[Constants.Metadata.Type],
    //             Position = (long)linkEvent.CommitPosition,
    //             Revision = (long)linkEvent.StreamRevision,
    //             Stream   = StreamName.From(linkEvent.StreamIdentifier!),
    //         }
    //         : Link.Unset;
    //
    //     IRecordDecoder decoder = new RecordDecoder(serializerProvider, registryPolicy);
    //
    //     var record = new Record(decoder) {
    //         Id        = recordId,
    //         Position  = RecordPosition.From(position, stream, revision),
    //         Sequence  = sequenceId,
    //         Timestamp = timestamp,
    //         Data      = data,
    //         Metadata  = metadata,
    //         Schema    = schema,
    //         Link      = link,
    //     };
    //
    //     if (!skipDecoding)
    //         await record.TryDecode(ct).ConfigureAwait(false);
    //
    //     return record;
    // }
    //
    // #endregion
}
