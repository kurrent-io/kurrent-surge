#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;
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
    public async ValueTask<Record> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
        if (position == LogPosition.Unset)
            throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be Unset.");

        var request = Requests.CreateInspectRecordRequest(position);

        var result = await ReadCore(request, skipDecoding, throwOnUnresolved: false, RecordFilter.None, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        
        // maybe add a strict option that will throw if the record is not found or if it was deleted or tombstoned
        // when strict is null it would return Record.None
        // or we could even return a result<record, exception> or IVariant<Record, RecordState>
        
        if (result.Value is not Record record)
            throw new LogPositionNotFoundException(position);

        // Check if the source stream has been deleted or tombstoned
        if (record.Stream != StreamName.Unset && !record.Stream.IsMetadataStream) {
            var streamInfo = await GetStreamInfo(record.Stream, cancellationToken).ConfigureAwait(false);

            switch (streamInfo.State) {
                case StreamState.Deleted:    throw new StreamDeletedException(record.Position.Stream);
                case StreamState.Tombstoned: throw new StreamTombstonedException(record.Position.Stream);
            }
        }
       
        return record;
    }

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
    public async ValueTask<Record> ReadStreamEdge(StreamName stream, ReadDirection direction, bool skipDecoding, CancellationToken cancellationToken) {
        if (stream == StreamName.Unset)
            throw new ArgumentException("Stream name cannot be None", nameof(stream));

        if (stream == StreamName.Log)
            throw new ArgumentException("Stream name cannot be `$all`", nameof(stream));

        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction, 1);

            var message = await ReadCore(request, skipDecoding, throwOnUnresolved: false, RecordFilter.None, cancellationToken)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            // Check the concrete value, not IsRecord: on an empty read FirstOrDefaultAsync returns a
            // default ReadRecordsResult whose IsRecord is true (variant default = case 0) with a null
            // payload, so `IsRecord` would leak that null instead of taking the throw branch.
            return message.Value is Record record
                ? record
                : throw new UnreachableException($"No record found at the specified stream edge: {stream} ({direction})");
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public IAsyncEnumerable<ReadRecordsResult> ReadRecords(ReadRecordsOptions options, CancellationToken ct = default) {
        options.EnsureValid();
        return ReadRecordsCore(options, ct);
    }

    public IAsyncEnumerable<ReadRecordsResult> ReadStream(StreamName stream, ReadStreamOptions options, CancellationToken ct = default) {
        // The stream used to live on the options object, so these guards (and the filter/stream
        // agreement check) were validator rules. With the stream now a method parameter, they move
        // here — mirroring the same checks ReadStreamEdge already performs against the parameter.
        if (stream == StreamName.Unset)
            throw new ArgumentException("Stream name must be specified when reading from a specific stream.", nameof(stream));

        if (stream == StreamName.Log)
            throw new ArgumentException("Stream name cannot be '$all' when reading from a specific stream.", nameof(stream));

        if (options.Filter is StreamNameFilter streamFilter && streamFilter.TargetStream != stream)
            throw new ArgumentException("Filter target stream does not match the specified stream to read from.", nameof(options));

        options.EnsureValid();

        return ReadStreamCore(stream, options, ct);
    }

    /// <summary>
    /// Routes a read by its TARGET (the filter decides: stream-name filter → direct stream
    /// read, anything else → the log) and makes sure the start position speaks the target's
    /// currency — translating via the start resolvers instead of silently discarding it.
    /// </summary>
    async IAsyncEnumerable<ReadRecordsResult> ReadRecordsCore(ReadRecordsOptions options, [EnumeratorCancellation] CancellationToken ct) {
        if (options.Filter.UnwrapFilter() is StreamNameFilter target) {
            var start = AlignSentinels(options.Start, target.TargetStream);

            // "From the end, forwards" reads nothing — and the stream wire cannot even
            // express it (End + Forwards is rejected server-side; subscriptions own that
            // shape). Short-circuit the empty read client-side.
            if (start.Revision == Revision.Max && options.Direction == ReadDirection.Forwards)
                yield break;

            // Native = the stream half names THIS target and carries a revision. A foreign
            // stream half (e.g. a $all checkpoint pointing at another stream's record) is
            // never trusted — the log half is resolved to the target's boundary instead.
            if (start.Stream != target.TargetStream || start.Revision == Revision.Unset) {
                if (start.LogPosition == LogPosition.Unset)
                    throw new ArgumentException(
                        "Start position cannot address the target stream: it carries neither a " +
                        "matching stream revision nor a log position to resolve one from.", nameof(options));

                start = await ResolveReadStart(
                        RecordPosition.From(start.LogPosition, target.TargetStream, Revision.Unset),
                        options.Direction, ct)
                    .ConfigureAwait(false);

                // No record of the stream qualifies from this position — the read is empty.
                if (start.IsUnset)
                    yield break;
            }

            options = options with { Start = start };
        }
        else if (options.Start.LogPosition == LogPosition.Unset) {
            // The log is addressed by position only. A stream-only start for a log read is
            // rejected by policy — guessing a position from a foreign revision is never safe.
            throw new ArgumentException("Log-targeted reads require a start with a log position.", nameof(options));
        }

        var request = Requests.CreateReadRecordsRequest(options);

        await foreach (var msg in ReadCore(request, options.SkipDecoding, options.ThrowOnUnresolved, options.Filter, ct).ConfigureAwait(false))
            yield return msg;
    }

    /// <summary>
    /// Single entry point: a direct stream read is a log read whose filter targets one stream.
    /// A caller-supplied non-stream filter re-routes through the log — the start revision is
    /// translated into its log position first (one stream read) instead of being discarded.
    /// </summary>
    async IAsyncEnumerable<ReadRecordsResult> ReadStreamCore(StreamName stream, ReadStreamOptions options, [EnumeratorCancellation] CancellationToken ct) {
        // Empty filter → synthesize a stream-name filter; any caller filter wins outright
        // (the guard above ensured an explicit StreamNameFilter agrees with the parameter).
        var filter = options.Filter is EmptyFilter
            ? StreamNameFilter.Parse(stream)
            : options.Filter;

        var start = RecordPosition.From(LogPosition.Unset, stream, options.Start);

        if (filter.UnwrapFilter() is not StreamNameFilter) {
            start = await ResolveReadStart(start, options.Direction, ct).ConfigureAwait(false);

            if (start.IsUnset)
                yield break;
        }

        var readOptions = new ReadRecordsOptions {
            Start             = start,
            Filter            = filter,
            Limit             = options.Limit,
            Direction         = options.Direction,
            SkipDecoding      = options.SkipDecoding,
            ThrowOnUnresolved = options.ThrowOnUnresolved,
            ResolveLinks      = options.ResolveLinks
        };

        await foreach (var msg in ReadRecordsCore(readOptions, ct).ConfigureAwait(false))
            yield return msg;
    }

    /// <summary>
    /// Sentinel starts speak both currencies: pin them to the target stream so the wire
    /// mapping resolves them natively (Earliest → first revision, Latest → end of stream).
    /// </summary>
    static RecordPosition AlignSentinels(RecordPosition start, StreamName target) =>
        start.LogPosition == LogPosition.Earliest ? RecordPosition.From(LogPosition.Earliest, target, Revision.Min)
        : start.LogPosition == LogPosition.Latest ? RecordPosition.From(LogPosition.Latest, target, Revision.Max)
        : start;

    async IAsyncEnumerable<ReadRecordsResult> ReadCore(
        ReadReq request, bool skipDecoding, bool throwOnUnresolved,
        RecordFilter filter, [EnumeratorCancellation] CancellationToken ct
    ) {
        var session = LegacyServiceClient.Read(request, cancellationToken: ct).All(TranslateError);

        var sequencer = new SequenceGenerator();

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

            ReadRecordsResult result;

            // No Checkpoint case on purpose: scan-window checkpoints are emitted by exactly
            // one server enumerator — AllSubscriptionFiltered.
            switch (rsp.ContentCase) {
                case CaughtUp:
                    result = rsp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                    break;
                case Event:
                    result = await rsp.Event
                        .MapToRecordAsync(sequencer.FetchNext(), CreateMappingContext(ct), skipDecoding, throwOnUnresolved)
                        .ConfigureAwait(false);

                    break;
                default:
                    continue;
            }

            // The filter alone decides whether the SDK re-evaluates it. A server-routable filter
            // (RequiresClientEvaluation == false) means the server already delivered exactly its
            // matches, in the resolution form the caller asked for — re-running IsMatch here would
            // second-guess the server routing and the ResolveLinks choice and silently drop records
            // the caller asked for (e.g. a $ce-/$et- read resolved to origins whose stream no longer
            // matches the filter's pattern). Only a client-evaluated filter re-checks each record.
            if (result.IsRecord && filter.RequiresClientEvaluation && !filter.IsMatch(result))
                continue;

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
    extension(StreamsClient client) {
        public ValueTask<Record> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region ->> Read Records <<-

        public IAsyncEnumerable<ReadRecordsResult> ReadRecords(LogPosition start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(new ReadRecordsOptions { Start = start }, cancellationToken);

        public IAsyncEnumerable<ReadRecordsResult> ReadRecords(Func<ReadRecordsOptions, ReadRecordsOptions> configure, CancellationToken cancellationToken = default) =>
            client.ReadRecords(configure(new ReadRecordsOptions()), cancellationToken);

        #endregion

        #region ->> Read Streams <<-

        public IAsyncEnumerable<ReadRecordsResult> ReadStream(StreamName stream, Func<ReadStreamOptions, ReadStreamOptions> configure, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, configure(new ReadStreamOptions()), cancellationToken);

        public IAsyncEnumerable<ReadRecordsResult> ReadStreamRecords(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, new ReadStreamOptions { Start = start }, cancellationToken);

        public IAsyncEnumerable<ReadRecordsResult> ReadStreamRecords(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadStream(stream, new ReadStreamOptions(), cancellationToken);

        public ValueTask<Record> ReadFirstStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Forwards, skipDecoding, cancellationToken);

        public ValueTask<Record> ReadLastStreamRecord(StreamName stream, bool skipDecoding, CancellationToken cancellationToken = default) =>
            client.ReadStreamEdge(stream, ReadDirection.Backwards, skipDecoding, cancellationToken);

        public ValueTask<Record> ReadFirstStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadFirstStreamRecord(stream, false, cancellationToken);

        public ValueTask<Record> ReadLastStreamRecord(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadLastStreamRecord(stream, false, cancellationToken);

        #endregion
    }

    extension(IAsyncEnumerable<ReadRecordsResult> source) {
        public ValueTask<List<Record>> ToListAsync() =>
            source.Where(msg => msg.IsRecord).Select(msg => msg.AsRecord).ToListAsync();
    }
}
