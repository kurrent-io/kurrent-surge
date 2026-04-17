#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Grpc;
using KurrentDB.Protocol.V1.Streams;
using Microsoft.Extensions.Logging;
using static Kurrent.Client.Streams.StreamsClientV1Mapper;
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

        var result = await ReadCore(request, skipDecoding, RecordFilter.None, cancellationToken)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

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

            var message = await ReadCore(request, skipDecoding, RecordFilter.None, cancellationToken)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return message.IsRecord 
                ? message.AsRecord 
                : throw new UnreachableException($"No record found at the specified stream edge: {stream} ({direction})");
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException();
        }
    }

    public IAsyncEnumerable<ReadRecordsResult> ReadRecords(ReadRecordsRequest request, CancellationToken ct = default) {
        request.EnsureValid();

        var grpcRequest = Requests.CreateReadRecordsRequest(request);

        return ReadCore(grpcRequest, request.SkipDecoding, request.Filter, ct);
    }

    public IAsyncEnumerable<ReadRecordsResult> ReadStream(ReadStreamRequest request, CancellationToken ct = default) {
        request.EnsureValid();

        var grpcRequest = Requests.CreateReadStreamRequest(request);

        return ReadCore(grpcRequest, request.SkipDecoding, request.Filter, ct);
    }

    async IAsyncEnumerable<ReadRecordsResult> ReadCore(
        ReadReq request, bool skipDecoding,
        IRecordFilter filter, [EnumeratorCancellation] CancellationToken ct
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

            switch (rsp.ContentCase) {
                case CaughtUp:
                    result = rsp.CaughtUp.MapToHeartbeat(sequencer.FetchNext());
                    break;
                case Event:
                    result = await rsp.Event
                        .MapToRecordAsync(
                            sequencer.FetchNext(),
                            SerializerProvider, MetadataDecoder,
                            SchemaRegistryPolicy.NoRequirements,
                            skipDecoding, ct)
                        .ConfigureAwait(false);

                    var record = result.AsRecord;

                    Logger.LogInformation(
                        "{Position} {Type}: {@Value}",
                        record.Position, record.ValueType.Name, record.Value
                    );

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
            client.ReadRecords(
                ReadRecordsRequest.New.FromPosition(start).Build(),
                cancellationToken
            );

        #endregion

        #region ->> Read Streams <<-

        public IAsyncEnumerable<ReadRecordsResult> ReadStreamRecords(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            client.ReadStream(
                ReadStreamRequest.New.ForStream(stream).FromRevision(start).Build(),
                cancellationToken
            );

        public IAsyncEnumerable<ReadRecordsResult> ReadStreamRecords(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadStream(
                ReadStreamRequest.New.ForStream(stream).Build(),
                cancellationToken
            );

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
