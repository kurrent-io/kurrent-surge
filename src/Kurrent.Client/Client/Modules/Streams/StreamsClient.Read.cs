#pragma warning disable CS8509
#pragma warning disable CS8524

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Grpc;
using KurrentDB.Protocol.V1.Streams;
using static Kurrent.Client.Streams.StreamsClientMapping;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Attempts to read a record at the specified log position.
    /// </summary>
    /// <param name="position">
    /// The log position to inspect. Must not be <see cref="LogPosition.Unset"/>. Use <see cref="LogPosition.Earliest"/> or <see cref="LogPosition.Latest"/> to inspect the first or last record respectively.
    /// </param>
    /// <param name="skipDecoding">
    /// If true, the record will not be deserialized.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// The record at <paramref name="position"/>, or <see cref="Record.None"/> when no record exists there.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="position"/> is <see cref="LogPosition.Unset"/>.
    /// </exception>
    /// <remarks>
    /// The record is returned even when its source stream has been soft-deleted or tombstoned: a log
    /// position addresses a physical record that remains readable until the log is scavenged. Link
    /// resolution is disabled, so the returned record is the one physically at the position rather
    /// than a link's resolved target.
    /// </remarks>
    public async ValueTask<Record> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
        if (position == LogPosition.Unset)
            throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be Unset.");

        var request = Requests.CreateInspectRecordRequest(position);

        var result = await ReadCore(request, skipDecoding, throwOnUnresolved: false, RecordFilter.None, DefaultMaxSearchWindow, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // A log position addresses a physical record that stays readable until scavenge, so a
        // soft-deleted or tombstoned source stream does not suppress it — the record is returned
        // as-is. Absence (an out-of-range position, or an empty log via Earliest/Latest) yields
        // Record.None instead of a throw: callers inspect the result rather than catching.
        return result.Value is Record record ? record : Record.None;
    }

    /// <summary>
    /// Reads the first record of the specified stream, or of the log when <paramref name="stream"/>
    /// is <see cref="StreamName.Log"/>.
    /// </summary>
    /// <param name="stream">
    /// The target to read from. A regular stream name reads that stream's first record;
    /// <see cref="StreamName.Log"/> reads the first record in the log.
    /// </param>
    /// <param name="skipDecoding">
    /// If true, the record's data and metadata will not be deserialized.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    public ValueTask<Record> FirstRecord(StreamName stream, bool skipDecoding = false, CancellationToken cancellationToken = default) =>
        stream == StreamName.Log
            ? InspectRecord(LogPosition.Earliest, skipDecoding, cancellationToken)
            : ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken);

    /// <summary>
    /// Reads the first record in the log.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    public ValueTask<Record> FirstRecord(CancellationToken cancellationToken = default) =>
        FirstRecord(StreamName.Log, skipDecoding: false, cancellationToken);

    /// <summary>
    /// Reads the last record of the specified stream, or of the log when <paramref name="stream"/>
    /// is <see cref="StreamName.Log"/>.
    /// </summary>
    /// <param name="stream">
    /// The target to read from. A regular stream name reads that stream's last record;
    /// <see cref="StreamName.Log"/> reads the last record in the log.
    /// </param>
    /// <param name="skipDecoding">
    /// If true, the record's data and metadata will not be deserialized.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    public ValueTask<Record> LastRecord(StreamName stream, bool skipDecoding = false, CancellationToken cancellationToken = default) =>
        stream == StreamName.Log
            ? InspectRecord(LogPosition.Latest, skipDecoding, cancellationToken)
            : ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken);

    /// <summary>
    /// Reads the last record in the log.
    /// </summary>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    public ValueTask<Record> LastRecord(CancellationToken cancellationToken = default) =>
        LastRecord(StreamName.Log, skipDecoding: false, cancellationToken);

    /// <summary>
    /// Reads the first or last record from a stream.
    /// </summary>
    /// <param name="stream">
    /// The name of the stream to read from.
    /// </param>
    /// <param name="direction">
    /// The direction in which to read the stream edge. Use <see cref="ReadDirection.Forwards"/> to read the first record, or <see cref="ReadDirection.Backwards"/> to read the last record.
    /// </param>
    /// <param name="skipDecoding">
    /// If true, the record's data and metadata will not be deserialized.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    async ValueTask<Record> ReadStreamEdge(StreamName stream, ReadDirection direction, bool skipDecoding = false, CancellationToken cancellationToken = default) {
        if (stream == StreamName.Unset)
            throw new ArgumentException("Stream name cannot be None", nameof(stream));

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all'.", nameof(stream));

        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction, 1);

            var message = await ReadCore(request, skipDecoding, throwOnUnresolved: false, RecordFilter.None, DefaultMaxSearchWindow, cancellationToken)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            // Check the concrete value, not IsRecord: on an empty read FirstOrDefaultAsync returns a
            // default ReadMessage whose IsRecord is true (variant default = case 0) with a null payload.
            // An empty edge is reachable when the stream exists but has no readable records (e.g. every
            // event MaxAge-expired, or the stream fully truncated): the server returns success with zero
            // events, not StreamNotFound. So mirror InspectRecord and return Record.None rather than throwing.
            return message.Value is Record record ? record : Record.None;
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public IAsyncEnumerable<ReadMessage> ReadRecords(ReadRecordsOptions options, CancellationToken ct = default) {
        options.EnsureValid();

        return ReadRecordsCore();

        // The filter routes the read to its target and ResolveStartPosition completes the start
        // into that target's currency: Unset = empty read (by definition, or nothing qualifies);
        // it throws when the start cannot address the target.
        async IAsyncEnumerable<ReadMessage> ReadRecordsCore() {
            var start = await ResolveStartPosition(options.Filter, options.Start, options.Direction, ct).ConfigureAwait(false);

            if (start.IsUnset)
                yield break;

            var request = Requests.CreateReadRecordsRequest(options with { Start = start });

            await foreach (var msg in ReadCore(request, options.SkipDecoding, options.ThrowOnUnresolved, options.Filter, (uint)options.FilterMaxSearchWindow, ct).ConfigureAwait(false))
                yield return msg;
        }
    }

    public IAsyncEnumerable<ReadMessage> ReadStream(StreamName stream, ReadStreamOptions options, CancellationToken ct = default) {
        // The stream used to live on the options object, so these guards (and the filter/stream
        // agreement check) were validator rules. With the stream now a method parameter, they move
        // here — mirroring the same checks ReadStreamEdge already performs against the parameter.
        if (stream == StreamName.Unset)
            throw new ArgumentException("Stream name must be specified when reading from a specific stream.", nameof(stream));

        if (stream.IsAllStream)
            throw new ArgumentException("Stream name cannot be '$all' when reading from a specific stream.", nameof(stream));

        if (options.Filter is StreamNameFilter streamFilter && streamFilter.TargetStream != stream)
            throw new ArgumentException("Filter target stream does not match the specified stream to read from.", nameof(options));

        options.EnsureValid();

        // A stream read is a log read whose filter targets one stream: an empty filter synthesizes
        // one, any caller filter wins (the guard above ensured an explicit StreamNameFilter agrees).
        // The bare revision becomes the start's stream half; ResolveStartPosition does the rest —
        // including the stream→log translation when a foreign filter re-routes the read through $all.
        var filter = options.Filter is EmptyFilter
            ? StreamNameFilter.Parse(stream)
            : options.Filter;

        return ReadRecords(new ReadRecordsOptions {
            Start                 = RecordPosition.From(LogPosition.Unset, stream, options.Start),
            Filter                = filter,
            Limit                 = options.Limit,
            Direction             = options.Direction,
            FilterMaxSearchWindow = options.FilterMaxSearchWindow,
            SkipDecoding          = options.SkipDecoding,
            ThrowOnUnresolved     = options.ThrowOnUnresolved,
            ResolveLinks          = options.ResolveLinks
        }, ct);
    }

    async IAsyncEnumerable<ReadMessage> ReadCore(
        ReadReq request, bool skipDecoding, bool throwOnUnresolved,
        RecordFilter filter, uint maxSearchWindow, [EnumeratorCancellation] CancellationToken ct
    ) {
        var session = LegacyServiceClient.Read(request, cancellationToken: ct).All(TranslateError);

        var sequencer = new SequenceGenerator();

        // The filter is the authority on whether the SDK evaluates it client-side. When it does, it
        // discards records the consumer never sees — invisible progress — so this read must raise
        // scan checkpoints of its own, exactly as the client-filtered subscription path does. The V1
        // read enumerators never emit filter checkpoints (only AllSubscriptionFiltered does), so a
        // long sparse client-filtered read would otherwise have no progress signal and no cheap
        // resume point. A server-routable filter leaves this off: the server delivers only matches.
        var filtersLocally = filter.RequiresClientEvaluation;

        // Records scanned since the last synthetic checkpoint. Unlike the subscription path there is
        // no server checkpoint to reset against — a V1 read emits none — so this only ever resets
        // when the SDK itself raises a checkpoint.
        uint scannedSinceCheckpoint = 0;

        await foreach (var rsp in session.WithCancellation(ct).ConfigureAwait(false)) {
            // v1 protocol bull: StreamNotFound is a message, not an exception (unlike 99.9% of other error cases)
            // And we dont know if the stream is missing or deleted until we check the stream info.
            // Meanwhile the stream state might have changed to deleted or tombstoned.
            if (rsp.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, ct).ConfigureAwait(false);

                // why are we throwing StreamNotFoundException on active?!
                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => new StreamNotFoundException(stream)
                };
            }

            ReadMessage result;

            // No Checkpoint case on purpose: scan-window checkpoints are emitted by exactly
            // one server enumerator — AllSubscriptionFiltered.
            switch (rsp.ContentCase) {
                case CaughtUp:
                    result = rsp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                    break;
                case Event:
                    var record = await rsp.Event
                        .MapToRecordAsync(sequencer.FetchNext(), CreateMappingContext(ct), skipDecoding, throwOnUnresolved)
                        .ConfigureAwait(false);

                    // Re-filter only when the filter runs client-side. A server-routable filter
                    // already delivered exactly its matches (per ResolveLinks), so re-running IsMatch
                    // would drop records the caller asked for — e.g. a $ce-/$et- read resolved to
                    // origin streams.
                    if (!filtersLocally || filter.IsMatch(record))
                        yield return record;

                    // When the SDK filters locally it discards records the consumer never sees, so —
                    // exactly like the client-filtered subscription — raise a scan checkpoint every
                    // window to carry progress across them. Emit AFTER delivering: the watermark is
                    // the position the scan has REACHED (a record already handed over), never one
                    // still to come. A read resumes from it INCLUSIVELY, re-reading that record — the
                    // dual of the subscription's exclusive "after" resume, and the read contract.
                    if (filtersLocally && ++scannedSinceCheckpoint >= maxSearchWindow) {
                        scannedSinceCheckpoint = 0;

                        // On $all the ReadEvent commit position is the physical scan watermark; with
                        // link resolution on (the default) the mapped record position points at an
                        // earlier target event, so a record-position watermark would regress while the
                        // scan advances. On a single-stream read the ReadEvent carries no commit
                        // position, so the record's own position is the scan position.
                        var scanPosition = rsp.Event.HasCommitPosition
                            ? LogPosition.From((long)rsp.Event.CommitPosition)
                            : record.Position.LogPosition;

                        yield return new FilterCheckpoint(CheckpointId.New(), record.Timestamp) {
                            Positions = [record.Position with { LogPosition = scanPosition }]
                        };
                    }

                    continue;
                default:
                    continue;
            }

            yield return result;
        }

        Exception TranslateError(RpcException rex) {
            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                return new StreamTombstonedException(stream);
            }

            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                return new LogPositionNotFoundException(position, rex);
            }

            return rex.AsWellKnownException();
        }
    }
}

public static partial class StreamsClientExtensions {
    extension(IStreamReader client) {
        public ValueTask<Record> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region ->> Read Records <<-

        public IAsyncEnumerable<ReadMessage> ReadRecords(LogPosition start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(new ReadRecordsOptions { Start = start }, cancellationToken);

        public IAsyncEnumerable<ReadMessage> ReadRecords(Func<ReadRecordsOptions, ReadRecordsOptions> configure, CancellationToken cancellationToken = default) =>
            client.ReadRecords(configure(new ReadRecordsOptions()), cancellationToken);

        #endregion

        #region ->> Read Streams <<-

        public IAsyncEnumerable<ReadMessage> ReadStream(StreamName stream, Func<ReadStreamOptions, ReadStreamOptions> configure, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, configure(new ReadStreamOptions()), cancellationToken);

        public IAsyncEnumerable<ReadMessage> ReadStream(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, new ReadStreamOptions { Start = start }, cancellationToken);

        public IAsyncEnumerable<ReadMessage> ReadStream(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, new ReadStreamOptions(), cancellationToken);

        #endregion
    }

    extension(IAsyncEnumerable<ReadMessage> source) {
        public ValueTask<List<Record>> ToListAsync() =>
            source.Where(msg => msg.IsRecord).Select(msg => msg.AsRecord).ToListAsync();
    }
}
