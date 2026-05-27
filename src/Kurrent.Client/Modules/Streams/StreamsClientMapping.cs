#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Collections;
using System.Diagnostics;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Client.Registry;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Client;

using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

/// <summary>
/// Bundles the dependencies and cancellation token needed by mapping extensions.
/// Created once per operation call — stack-allocated, zero GC pressure.
/// </summary>
readonly record struct MappingContext(
    ISchemaSerializerProvider Serializer,
    IMetadataDecoder MetadataDecoder,
    SchemaRegistryPolicy Policy,
    CancellationToken CancellationToken
);

static partial class StreamsClientMapping {
    /// <summary>
    /// Default number of raw log records the server scans per filter page before returning results.
    /// Tuned for high-throughput streaming — large enough to minimize round trips on sparse filters,
    /// bounded enough to avoid unbounded server work per page.
    /// </summary>
    internal const uint DefaultMaxSearchWindow = 4096;

    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly KurrentDB.Protocol.V1.Empty                               DefaultEmpty               = new();

    internal static partial class Requests {
        /// <summary>
        /// Reads backwards from the end of a stream. Used by both Read and Manage operations.
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
        /// Reads forwards from the start of a stream. Used by both Read and Manage operations.
        /// After a scavenge the first surviving event may not be at revision 0.
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

    static Contracts.ReadReq.Types.Options.Types.ReadDirection ToReadDirection(ReadDirection direction) =>
        direction switch {
            ReadDirection.Backwards => Contracts.ReadReq.Types.Options.Types.ReadDirection.Backwards,
            ReadDirection.Forwards  => Contracts.ReadReq.Types.Options.Types.ReadDirection.Forwards,
        };
    
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

    #region ->> Response Mapping <<-
    
    public static IntervalCheckpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
        var position = LogPosition.From((long)checkpoint.CommitPosition);

        return new(CheckpointId.New(), checkpoint.Timestamp.ToDateTimeOffset()) {
            Positions = [RecordPosition.From(position)]
        };
    }
    
    public static CaughtUpCheckpoint MapToCheckpoint(this Contracts.ReadResp.Types.CaughtUp caughtUp, SequenceId sequenceId) {
        var position = caughtUp.Position is not null ? LogPosition.From((long)caughtUp.Position.CommitPosition) : LogPosition.Unset;
        var revision = caughtUp.HasStreamRevision ? Revision.From(caughtUp.StreamRevision) : Revision.Unset;

        return new(CheckpointId.New(), caughtUp.Timestamp.ToDateTimeOffset()) {
            Positions = [RecordPosition.From(position, StreamName.Unset, revision)]
        };
    }

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
        MappingContext ctx,
        bool skipDecoding = false
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

        var metadata = await ctx.MetadataDecoder
            .Decode(recorded.CustomMetadata.Memory, new(stream, schemaName, schemaFormat))
            .ConfigureAwait(false);

        var schema = metadata.ExtractSchemaInfo();

        IRecordDecoder decoder = new RecordDecoder(ctx.Serializer, ctx.Policy);

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
            await record.TryDecode(ctx.CancellationToken).ConfigureAwait(false);

        return record;
    }

    #endregion // Response Mapping

    #region ->> Metadata/Properties Mapping <<-

    [PublicAPI]
    public static Metadata ToMetadata(this MapField<string, Value> source) {
        return source.Aggregate(new Metadata(), (meta, entry) => meta.With(entry.Key, MapToMetadataValue(entry.Value)));

        static object? MapToMetadataValue(Value source) =>
            source.KindCase switch {
                Value.KindOneofCase.NullValue   => null,
                Value.KindOneofCase.None        => null,
                Value.KindOneofCase.StringValue => source.StringValue,
                Value.KindOneofCase.BoolValue   => source.BoolValue,
                Value.KindOneofCase.NumberValue => source.NumberValue,
                Value.KindOneofCase.StructValue => source.StructValue.Fields.ToMetadata(),
                Value.KindOneofCase.ListValue   => source.ListValue.Values.Select(MapToMetadataValue)
            };
    }

    public static MapField<string, Value> ToProperties(this Metadata source) {
        return source.Aggregate(
            new MapField<string, Value>(), (seed, entry) => {
                seed.Add(entry.Key, ToValue(entry.Value));
                return seed;
            });
        
        // Maps a CLR value to a protobuf <see cref="Value"/>.
        // Byte arrays are treated as base64 strings by default. Protobuf only supports double
        // as a number type, so all numeric types are widened via <see cref="Convert.ToDouble(object)"/>.
        static Value ToValue(object? source, bool treatByteArrayAsString = false) {
            if (IsNumber(source))
                return new() { NumberValue = Convert.ToDouble(source) };

            if (source is IEnumerable and not string and not byte[] and not Metadata) {
                var values = ((IEnumerable)source).Cast<object?>().Select(val => ToValue(val, treatByteArrayAsString));
                return new() { ListValue = new() { Values = { values } } };
            }

            return source switch {
                null       => new() { NullValue   = NullValue.NullValue },
                string x   => new() { StringValue = x },
                bool x     => new() { BoolValue   = x },
                Metadata x => new() { StructValue = new() { Fields = { x.ToProperties() } } },

                ReadOnlyMemory<byte> x => new() { StringValue = Convert.ToBase64String(x.Span) },
                Memory<byte> x         => new() { StringValue = Convert.ToBase64String(x.Span) },
                ArraySegment<byte> x   => new() { StringValue = Convert.ToBase64String(x.Array ?? x.ToArray()) },
                byte[] x               => new() { StringValue = Convert.ToBase64String(x) },

                _ => new() { StringValue = source.ToString() }
            };

            static bool IsNumber(object? source) =>
                source is int or double or float or decimal or long or short or byte or sbyte or ushort or uint or ulong;
        }
    }

    #endregion // Metadata/Properties Mapping
}
