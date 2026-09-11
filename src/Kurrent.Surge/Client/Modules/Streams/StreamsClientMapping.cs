#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable CS8509 // The switch expression does not handle all possible values of its input type (it is not exhaustive).

using System.Collections;
using System.Diagnostics;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Client.Legacy;
using Kurrent.Surge.Schema;
using Kurrent.Surge.Schema.Catalogs;

using Contracts = KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Surge.Client.Streams;

/// <summary>
/// Bundles the dependencies and cancellation token needed by mapping extensions.
/// Created once per operation call — stack-allocated, zero GC pressure.
/// </summary>
readonly record struct MappingContext(
    IMetadataDecoder MetadataDecoder,
    IRecordDecoder Decoder,
    CancellationToken CancellationToken
);

static partial class StreamsClientMapping {
    /// <summary>
    /// Default number of raw log records the server scans per filter page before returning results.
    /// Tuned for high-throughput streaming — large enough to minimize round trips on sparse filters,
    /// bounded enough to avoid unbounded server work per page.
    /// </summary>
    // Forwards to the single source in the contract layer (StreamDefaults), where the option
    // types that default to it now live. Kept here so the existing StreamsClientMapping callers
    // (and `using static StreamsClientMapping`) stay unchanged.
    internal const int DefaultMaxSearchWindow = StreamDefaults.DefaultMaxSearchWindow;

    static readonly Contracts.ReadReq.Types.Options.Types.ControlOption       DefaultControlOption       = new() { Compatibility = 1 };
    static readonly Contracts.ReadReq.Types.Options.Types.SubscriptionOptions DefaultSubscriptionOptions = new();
    static readonly Contracts.ReadReq.Types.Options.Types.UUIDOption          DefaultUuidOptions         = new();
    static readonly KurrentDB.Protocol.V1.Empty                               DefaultEmpty               = new();

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
    ///   IndexFilter             Options.All(start.LogPosition) + Filter.StreamIdentifier.Prefix(IndexName)
    ///   StreamRegexFilter       Options.All(start.LogPosition) + Filter.StreamIdentifier.Regex(Expression)
    ///   SchemaRegexFilter       Options.All(start.LogPosition) + Filter.EventType.Regex(Expression)
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
        //   SchemaRegexFilter  → EventType.Regex         (filtered $all scan by event type)
        //   Anything else      → null (caller emits NoFilter)
        static Contracts.ReadReq.Types.Options.Types.FilterOptions? ConvertToFilterOptions(
            RecordFilter f, uint maxSearchWindow
        ) {
            Contracts.ReadReq.Types.Options.Types.FilterOptions? options = f switch {
                IndexFilter idx     => new() { StreamIdentifier = new() { Prefix = { idx.IndexName } } },
                StreamRegexFilter r => new() { StreamIdentifier = new() { Regex = r.Expression } },
                SchemaRegexFilter r => new() { EventType        = new() { Regex = r.Expression } },
                _                   => null
            };

            if (options is null)
                return null;

            // FilterOptions.window is a oneof of `max` (uint32) or `count` (Empty → server default = 32).
            // We always use `max`: the server default is far too small for sparse filters (excessive
            // round trips on near-empty pages) and floods subscriptions with FilterCheckpoints
            // (one every `multiplier × window` scanned records). Explicit `max` gives the SDK full
            // control independent of server-side defaults that may change between versions.
            options.Max = maxSearchWindow;
            // multiplier = 1 keeps the relationship simple: one FilterCheckpoint per scan window.
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
    
    public static FilterCheckpoint MapToCheckpoint(this Contracts.ReadResp.Types.Checkpoint checkpoint, SequenceId sequenceId) {
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
        bool skipDecoding = false,
        bool throwOnUnresolved = false
    ) {
        var recorded = readEvent.Event ?? readEvent.Link
            ?? throw new UnreachableException("ReadEvent has neither Event nor Link.");

        var isLink = readEvent.Event is null;

        var link = readEvent is { Link: { } linkEvent, Event: not null }
            ? new Link {
                Name     = linkEvent.Metadata[Constants.Metadata.Type],
                Position = RecordPosition.From( 
                    (long)linkEvent.CommitPosition, 
                    StreamName.From(linkEvent.StreamIdentifier!), 
                    (long)linkEvent.StreamRevision
                )
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

        // Decoded metadata that embeds its own schema keys (link records, custom decoders)
        // wins over the wire values; the common case never touches the dictionary.
        var schema = metadata.ContainsKey(SystemMetadataKeys.SchemaName)
            ? metadata.ExtractSchemaInfo()
            : new RecordSchemaInfo { Schema = schemaName, DataFormat = schemaFormat };

        var record = new Record(ctx.Decoder) {
            Id        = RecordId.From(recordId),
            Position  = RecordPosition.From(position, stream, revision),
            Timestamp = timestamp,
            Data      = data,
            Metadata  = metadata,
            Schema    = schema,
            Link      = link,
            IsLink    = isLink
        };

        if (!skipDecoding) {
            var outcome = await record.TryDecode(ctx.CancellationToken).ConfigureAwait(false);

            // Per-operation strictness: an unknown schema name is data by default (the
            // record passes through raw); strict reads/subscriptions opt into failing.
            if (outcome == DecodeOutcome.UnknownType && throwOnUnresolved)
                throw new MessageTypeResolutionException(record.Schema.Schema);
        }

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
        var properties = new MapField<string, Value>();
        source.CopyToProperties(properties);
        return properties;
    }

    /// <summary>
    /// Writes the metadata entries straight into an existing properties map — the message's
    /// own map on the append path — without materializing an intermediate one.
    /// </summary>
    internal static void CopyToProperties(this Metadata source, MapField<string, Value> target) {
        // Enumerates the backing dictionary directly: Metadata only exposes the interface
        // enumerator, which boxes on every call, and this path runs once per record.
        foreach (var entry in source.Dictionary)
            target.Add(entry.Key, ToValue(entry.Value));
    }

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
            Metadata x => ToStructValue(x),

            ReadOnlyMemory<byte> x => new() { StringValue = Convert.ToBase64String(x.Span) },
            Memory<byte> x         => new() { StringValue = Convert.ToBase64String(x.Span) },
            ArraySegment<byte> x   => new() { StringValue = Convert.ToBase64String(x.Array ?? x.ToArray()) },
            byte[] x               => new() { StringValue = Convert.ToBase64String(x) },

            _ => new() { StringValue = source.ToString() }
        };

        static Value ToStructValue(Metadata source) {
            var value = new Value { StructValue = new() };
            source.CopyToProperties(value.StructValue.Fields);
            return value;
        }

        static bool IsNumber(object? source) =>
            source is int or double or float or decimal or long or short or byte or sbyte or ushort or uint or ulong;
    }

    #endregion // Metadata/Properties Mapping
}
