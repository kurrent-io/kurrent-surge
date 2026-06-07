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
        return ReadCore(Requests.CreateReadRecordsRequest(options), options.SkipDecoding, options.ThrowOnUnresolved, options.Filter, ct);
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

        var grpcRequest = Requests.CreateReadStreamRequest(stream, options);

        return ReadCore(grpcRequest, options.SkipDecoding, options.ThrowOnUnresolved, options.Filter, ct);
    }

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

            if (result.IsRecord && !filter.IsMatch(result))
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
