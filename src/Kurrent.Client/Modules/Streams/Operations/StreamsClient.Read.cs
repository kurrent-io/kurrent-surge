#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Protocol.V1.Streams;
using static System.StringComparison;
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

        try {
            var request = Requests.CreateInspectRecordRequest(position);

            await using var messages = await ReadCore(request, 1, skipDecoding, RecordFilter.None, cancellationToken)
                .ConfigureAwait(false);

            var message = await messages
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (message.Value is not Record record)
                throw new LogPositionNotFoundException(position);

            // Check if the source stream has been deleted or tombstoned
            if (record.Position.Stream != StreamName.Unset) {
                var streamInfo = await GetStreamInfo(record.Position.Stream, cancellationToken)
                    .ConfigureAwait(false);

                switch (streamInfo.State) {
                    case StreamState.Deleted:    throw new StreamDeletedException(record.Position.Stream);
                    case StreamState.Tombstoned: throw new StreamTombstonedException(record.Position.Stream);
                }
            }

            return record;
        }
        catch (Exception ex) {
            throw ex.AsWellKnownException(
                StreamTombstonedException.From,
                StreamDeletedException.From,
                LogPositionNotFoundException.From);
        }

        // {
        //     "$v": "3:-1:1:4",
        //     "$c": 12961,
        //     "$p": 12961,
        //     "$o": "TicTacToe-bad03a1c87c6",
        //     "$causedBy": "0197d630-9297-7000-9806-85795845c414"
        // }
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
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction);

            await using var messages = await ReadCore(request, 1, skipDecoding, RecordFilter.None, cancellationToken)
                .ConfigureAwait(false);

            var message = await messages.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (message.IsRecord)
                return message.AsRecord;
            else {
                throw new UnreachableException($"No record found at the specified stream edge: {stream} ({direction})");
            }
        }
        catch (Exception ex) {
            throw ex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                LogPositionNotFoundException.From);
        }
    }

    public ValueTask<RecordStream> ReadRecords(ReadOptions options) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadLogRequest(options);

        return ReadCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter,
            options.CancellationToken
        );
    }

    public ValueTask<RecordStream> ReadRecords(ReadStreamOptions options) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadStreamRequest(options);

        return ReadCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter,
            options.CancellationToken
        );
    }

    async ValueTask<RecordStream> ReadCore(
        ReadReq request, int bufferCapacity, bool skipDecoding,
        IRecordFilter filter, CancellationToken cancellationToken
    ) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        LogPosition position = LogPosition.From(request.Options.All.Position?.CommitPosition);

        try {
            var advanced = await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream), // we cant read it. yet we can subscribe to it
                    StreamState.Tombstoned => new StreamTombstonedException(stream),

                    // how is this even possible? Race condition between read and get info?
                    // Maybe the stream was just created after we got the not found response?
                    // We could retry the read probably but we will let the user handle it fro now
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (!advanced) {
                throw new LogPositionNotFoundException(
                    request.Options.All.Position?.CommitPosition ?? LogPosition.Unset,
                    $"There is no record at the specified position"
                );
            }
        }
        catch (RpcException rex) {
            session.Dispose();
            cancellator.Dispose();

            // handle cancellation properly
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            // when stream is tombstoned an exception is thrown for some weird reason
            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") { //rex.Status.Detail.Contains("is deleted")
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                // var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                var details  = rex.Message.Split(">>").Last().Trim();
                throw new LogPositionNotFoundException(position, details);
            }

            throw rex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                LogPositionNotFoundException.From);
        }
        catch (Exception ex) {
            session.Dispose();
            cancellator.Dispose();
            throw ex.ToException(
                StreamNotFoundException.From,
                StreamDeletedException.From,
                StreamTombstonedException.From,
                LogPositionNotFoundException.From);
        }

        var channelFactory = new Func<Channel<ReadRecordsResult>>(
            () => StartMessageRelay(session, bufferCapacity, skipDecoding, filter, cancellator));

        return new RecordStream(channelFactory);
    }

    Channel<ReadRecordsResult> StartMessageRelay(
        AsyncServerStreamingCall<ReadResp> session, int bufferCapacity,
        bool skipDecoding, IRecordFilter filter, CancellationTokenSource cancellator
    ) {
        var channel = Channel.CreateBounded<ReadRecordsResult>(
            new BoundedChannelOptions(bufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            }
        );

        var stoppingToken = cancellator.Token;

        // start pumping messages
        _ = Task.Run(PumpMessages, stoppingToken);

        return channel;

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask PumpMessages() {
            SequenceGenerator sequencer = new();

            try {
                // must continue from the cursor because we had to try to read
                // the first entry to check if the stream existed or if access was granted
                do {
                    var rsp = session.ResponseStream.Current;

                    ReadRecordsResult result;

                    switch (rsp?.ContentCase) {
                        case CaughtUp:
                            result = rsp.CaughtUp.MapToHeartbeat(sequencer.FetchNext());
                            break;
                        case Event:
                            result = await rsp.Event
                                .MapToRecordAsync(
                                    sequencer.FetchNext(),
                                    SerializerProvider, MetadataDecoder,
                                    SchemaRegistryPolicy.NoRequirements,
                                    skipDecoding, stoppingToken)
                                .ConfigureAwait(false);
                            break;
                        default:
                            continue; // Skip irrelevant, no allocation
                    }

                    // apply client-side filter if present
                    if (result.IsRecord && !filter.IsMatch(result))
                        continue;

                    // write to channel - backpressure via bounded channel
                    await channel.Writer.WriteAsync(result, stoppingToken).ConfigureAwait(false);

                } while (await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));

                channel.Writer.TryComplete();
            }
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException) {
                channel.Writer.TryComplete();
            }
            catch (Exception ex) {
                channel.Writer.TryComplete(ex);
            }
            finally {
                session.Dispose();
                cancellator.Dispose();
            }
        }
    }
}

public static partial class StreamsClientExtensions {
    extension(StreamsClient client) {
        public ValueTask<Record> InspectRecord(LogPosition position, CancellationToken cancellationToken = default) =>
            client.InspectRecord(position, true, cancellationToken);

        #region ->> Read Log <--

        public ValueTask<RecordStream> ReadRecords(LogPosition start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadLog.From(start)
                    .WithCancellationToken(cancellationToken)
                    .Build()
            );

        #endregion

        #region ->> Read Streams <<-

        public ValueTask<RecordStream> ReadStreamRecords(StreamName stream, Revision start, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadStream.From(stream)
                    .FromRevision(start)
                    .WithCancellationToken(cancellationToken)
                    .Build()
            );

        public ValueTask<RecordStream> ReadStreamRecords(StreamName stream, CancellationToken cancellationToken = default) =>
            client.ReadRecords(
                ReadStream.From(stream)
                    .WithCancellationToken(cancellationToken)
                    .Build()
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
}
