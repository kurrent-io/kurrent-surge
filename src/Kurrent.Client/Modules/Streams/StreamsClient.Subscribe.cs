using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Variant;
using KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Client.Streams;

// Checkpoint is a barrier (coordination token). Scan-progress cases (FilterCheckpoint,
// CaughtUpCheckpoint) carry a confirmed scan position; the client-side interval timer and
// processor-synthesized cases (IntervalCheckpoint, ManualCheckpoint, ShutdownCheckpoint, etc.)
// leave it empty.
// Streaming extends this hierarchy without touching client.
public abstract record Checkpoint(CheckpointId Id, DateTimeOffset Timestamp) {
    public IReadOnlyList<RecordPosition> Positions { get; init; } = [];
}

public sealed record IntervalCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

/// <summary>
/// Marks scan progress on a filtered subscription over the log: the scan has reached this log
/// position with no further matches in the window. Carries the scan watermark in
/// <see cref="Checkpoint.Positions"/>; paced by the subscription's max search window.
/// </summary>
/// <remarks>
/// Emitted by the server when the filter is expressed on the wire. When the filter cannot be —
/// and the SDK takes over filtering client-side — the SDK synthesizes these at the same cadence
/// so progress through barren stretches is never lost.
/// </remarks>
public sealed record FilterCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

public sealed record CaughtUpCheckpoint(CheckpointId Id, DateTimeOffset Timestamp)
    : Checkpoint(Id, Timestamp);

/// <summary>
/// Represents a unique session identifier assigned by the server when a subscription is established.
/// </summary>
[PublicAPI]
public readonly record struct SessionId {
    /// <summary>
    /// Represents an unset session (e.g., drone never connected).
    /// </summary>
    public static readonly SessionId Unset = new(Guid.Empty);

    SessionId(Guid value) => Value = value;

    /// <summary>
    /// The underlying GUID value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Indicates whether this session ID is unset.
    /// </summary>
    public bool IsUnset => this == Unset;

    /// <summary>
    /// Creates a <see cref="SessionId"/> from a GUID value.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is <see cref="Guid.Empty"/>.</exception>
    public static SessionId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("SessionId cannot be empty.", nameof(value))
            : new(value);

    public static implicit operator Guid(SessionId _) => _.Value;
    public static implicit operator SessionId(Guid _) => From(_);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Unique identifier for a checkpoint.
/// </summary>
/// <remarks>
/// Checkpoint IDs are coordination tokens, not positions.
/// Drones use these IDs to identify which checkpoint they're responding to.
/// </remarks>
[PublicAPI]
public readonly record struct CheckpointId(string Value) {
    public static readonly CheckpointId Unset = new("");

    /// <summary>Creates a new unique checkpoint ID.</summary>
    public static CheckpointId New() => new(Guid.NewGuid().ToString());

    public static CheckpointId From(string value) {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new CheckpointId(value);
    }

    public static CheckpointId From(ulong value) =>
        new(value.ToString());

    public static CheckpointId From(long value) =>
        new(value.ToString());

    /// <summary>True if this checkpoint ID is empty or unset.</summary>
    public bool IsUnset => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;

    public static implicit operator string(CheckpointId id) => id.Value;
}

public readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly SubscribeMessage None;
}

public readonly record struct Connected(SessionId SessionId);

public record SubscribeOptions {
    public RecordFilter   Filter { get; init; } = RecordFilter.None;
    public RecordPosition Start  { get; init; } = RecordPosition.Latest;

    public bool SkipDecoding    { get; init; }
    public bool ResolveLinks    { get; init; } = true;
    public uint MaxSearchWindow { get; init; } = StreamsClientMapping.DefaultMaxSearchWindow;

    /// <summary>
    /// When true, a record whose schema name has no mapped type fails the subscription
    /// instead of passing through raw. Default false: a forward-unknown type is data for
    /// fallback handlers, not a crashed subscription. Has no effect when
    /// <see cref="SkipDecoding"/> is set.
    /// </summary>
    public bool ThrowOnUnresolved { get; init; }
}

public partial class StreamsClient {
    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        SubscribeOptions options,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var start = options.Start;

        // Same target routing as reads: a stream-name filter targets a single stream, whose
        // wire currency is a revision. A start whose stream half doesn't name THIS target
        // (e.g. a $all checkpoint pointing at a foreign record) is never trusted — its log
        // half is resolved to the last processed record of the target instead, so the
        // exclusive After(...) resume is exact.
        if (options.Filter.UnwrapFilter() is StreamNameFilter target) {
            // Sentinels speak both currencies: pin them to the target so the wire maps them
            // natively (Earliest → first revision, Latest → end of stream).
            start =
                start.LogPosition == LogPosition.Earliest ? RecordPosition.From(LogPosition.Earliest, target.TargetStream, Revision.Min)
                : start.LogPosition == LogPosition.Latest ? RecordPosition.From(LogPosition.Latest, target.TargetStream, Revision.Max)
                : start;

            // bug? start.Stream is now target.TargetStream cause the sentinel alignment above just overrode the variable
            if (start.Stream != target.TargetStream || start.Revision == Revision.Unset) {
                if (start.LogPosition == LogPosition.Unset)
                    throw new ArgumentException(
                        "Start position cannot address the target stream: it carries neither a " +
                        "matching stream revision nor a log position to resolve one from.", nameof(options));

                var boundary = await ResolveSubscriptionStart(
                        RecordPosition.From(start.LogPosition, target.TargetStream, Revision.Unset), ct)
                    .ConfigureAwait(false);

                // None = nothing of the stream processed yet → subscribe from the start of
                // the stream (deliver everything), NOT an empty subscription.
                start = boundary.IsUnset
                    ? RecordPosition.From(LogPosition.Unset, target.TargetStream, Revision.Min)
                    : boundary;
            }
        }
        else if (start.LogPosition == LogPosition.Unset) {
            // The log is addressed by position only — never guess from a stream half.
            throw new ArgumentException("Log-targeted subscriptions require a start with a log position.", nameof(options));
        }

        var request = StreamsClientMapping.Requests.CreateSubscriptionRequest(
            options.Filter, start, options.ResolveLinks, options.MaxSearchWindow);

        AsyncServerStreamingCall<ReadResp> grpcSession;
        SessionId                          sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = LegacyServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"EVERYTHING IS BROKEN! First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}");

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                throw new LogPositionNotFoundException(position, rex);
            }

            throw rex.AsWellKnownException();
        }

        yield return new Connected(sessionId);

        SequenceGenerator sequencer = new();

        // The filter is the authority on whether the SDK evaluates it client-side. When it does,
        // it discards records the consumer never sees — invisible progress — so this subscription
        // must raise scan checkpoints of its own, exactly as a server-side filtered subscription
        // would. A purely server-routable filter leaves this off: the server delivers only matches
        // and (on a filtered $all) raises its own checkpoints.
        var filtersLocally = options.Filter.RequiresClientEvaluation;

        // Records received since the last checkpoint of any kind. A server checkpoint (filter or
        // caught-up) resets it: progress is already accounted for up to the server's watermark, so
        // the count restarts from there — "count locally since the last server-sent one".
        uint scannedSinceCheckpoint = 0;

        try {
            while (await grpcSession.ResponseStream.MoveNextSafe(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint:
                        // Server watermark — covers progress up to here, so reset the local window.
                        scannedSinceCheckpoint = 0;
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.CaughtUp: {
                        scannedSinceCheckpoint = 0;

                        var checkpoint = resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                        var position   = checkpoint.Positions[0];

                        // A stream-subscription CaughtUp carries the stream revision but no log position: it
                        // marks the live end, where no record exists yet (the revision is the next-expected,
                        // not a written record). Resolve the watermark by reading the stream's last record so
                        // every server checkpoint carries a log position, uniform with FilterCheckpoint — the
                        // processor then treats all server watermarks the same, by type, with no special-casing.
                        if (position.LogPosition == LogPosition.Unset && position.Revision != Revision.Unset) {
                            // The V1 CaughtUp message carries the stream revision but no stream name
                            // (proto/kurrentdb/protocol/v1/streams.proto CaughtUp), so the mapped position has an
                            // unset stream. This branch only fires for a stream subscription, so recover the name
                            // from the request — same source as the StreamNotFound branch in the connection phase.
                            StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                            // Read the stream's last record for the watermark. Use None: a subscription winding
                            // down (cancellation) must not abort this bounded metadata read and leave the
                            // checkpoint without a log position. If the stream has no records, keep it as-is.
                            var last = await ReadStream(
                                    stream,
                                    new ReadStreamOptions { Start = Revision.Max, Direction = ReadDirection.Backwards, Limit = 1, SkipDecoding = true },
                                    CancellationToken.None)
                                .Where(r => r.IsRecord)
                                .FirstOrDefaultAsync(CancellationToken.None)
                                .ConfigureAwait(false);

                            // Check the concrete value, not IsRecord: a default ReadMessage (empty
                            // read) reports IsRecord == true with a null payload.
                            if (last.Value is Record lastRecord)
                                checkpoint = checkpoint with { Positions = [lastRecord.Position] };
                        }

                        yield return checkpoint;
                        break;
                    }

                    case ReadResp.ContentOneofCase.Event:
                        var ctx = CreateMappingContext(ct);
                        var record = await resp.Event
                            .MapToRecordAsync(sequencer.FetchNext(), ctx, options.SkipDecoding, options.ThrowOnUnresolved)
                            .ConfigureAwait(false);

                        // Same gating as the read path: when the filter is fully server-side
                        // (filtersLocally == false) the server already delivered exactly its matches,
                        // resolved per ResolveLinks — re-running IsMatch would drop records the server
                        // matched. Only a client-evaluated filter re-checks each record here.
                        if (!filtersLocally || options.Filter.IsMatch(record))
                            yield return record;

                        // When the SDK filters locally, raise a scan checkpoint every window —
                        // matched or not, the record was evaluated, so the watermark can move past
                        // it. This carries progress across the records discarded above, which the
                        // consumer never sees and so could never checkpoint on. (When the filter is
                        // fully server-side this is off: deliveries advance progress on their own,
                        // and a filtered $all already gets server checkpoints.)
                        if (filtersLocally && ++scannedSinceCheckpoint >= options.MaxSearchWindow) {
                            scannedSinceCheckpoint = 0;

                            // Carry the full record position (stream + revision), but pin the log
                            // half to the position the scan has physically reached. On $all that is
                            // the ReadEvent-level commit position — link resolution (on by default)
                            // maps the record position to an earlier target event, so a
                            // record-position watermark would regress while the scan advances. On a
                            // stream subscription the ReadEvent carries no log position, so the
                            // record's own position is the scan position.
                            var scanPosition = resp.Event.HasCommitPosition
                                ? LogPosition.From((long)resp.Event.CommitPosition)
                                : record.Position.LogPosition;

                            yield return new FilterCheckpoint(CheckpointId.New(), record.Timestamp) {
                                Positions = [record.Position with { LogPosition = scanPosition }]
                            };
                        }

                    break;
                }
            }
        }
        finally {
            grpcSession.Dispose();
        }
    }
}
