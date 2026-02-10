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
    public async ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
        if (position == LogPosition.Unset)
            throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be Unset.");

        try {
            var request = Requests.CreateInspectRecordRequest(position);

            await using var messages = await ReadCore(request, 1, skipDecoding, RecordFilter.None, cancellationToken)
                .UnwrapAsync().ConfigureAwait(false);

            var message = await messages
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (message.Value is not Record record)
                return Result<Record, InspectRecordError>.Failure(
                    new LogPositionNotFoundException(position));

            // Check if the source stream has been deleted or tombstoned
            if (record.Position.Stream != StreamName.None) {
                var streamInfo = await GetStreamInfo(record.Position.Stream, cancellationToken)
                    .UnwrapAsync().ConfigureAwait(false);

                return streamInfo.State switch {
                    StreamState.Deleted    => Result<Record, InspectRecordError>.Failure(new StreamDeletedException(record.Position.Stream)),
                    StreamState.Tombstoned => Result<Record, InspectRecordError>.Failure(new StreamTombstonedException(record.Position.Stream)),
                    _                      => record
                };
            }

            return record;
        }
        catch (Exception ex) {
            return InspectRecordError.FromEx(ex);
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
    public async ValueTask<Result<Record, ReadError>> ReadStreamEdge(StreamName stream, ReadDirection direction, bool skipDecoding, CancellationToken cancellationToken) {
        if (stream == StreamName.None)
            throw new ArgumentException("Stream name cannot be None", nameof(stream));

        if (stream == StreamName.All)
            throw new ArgumentException("Stream name cannot be `$all`", nameof(stream));

        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction);

            await using var messages = await ReadCore(request, 1, skipDecoding, RecordFilter.None, cancellationToken)
                .UnwrapAsync().ConfigureAwait(false);

            var message = await messages.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (message.IsRecord)
                return message.AsRecord;

            return Result<Record, ReadError>.Failure(new UnreachableException($"No record found at the specified stream edge: {stream} ({direction})"));
        }
        catch (Exception ex) {
            return ReadError.FromEx(ex);
        }
    }

    public ValueTask<Result<RecordStream, ReadError>> ReadRecords(ReadOptions options) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadLogRequest(options);

        return ReadCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter,
            options.CancellationToken
        );
    }

    public ValueTask<Result<RecordStream, ReadError>> ReadRecords(ReadStreamOptions options) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadStreamRequest(options);

        return ReadCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter,
            options.CancellationToken
        );
    }

    public ValueTask<Result<RecordBatchStream, ReadError>> ReadBatches(
        ReadOptions options, int batchSize, TimeSpan batchTimeout
    ) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadLogRequest(options);

        return ReadBatchCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter, batchSize, batchTimeout,
            options.CancellationToken
        );
    }

    public ValueTask<Result<RecordBatchStream, ReadError>> ReadBatches(
        ReadStreamOptions options, int batchSize, TimeSpan batchTimeout
    ) {
        options.ValidateOptions().Unwrap();

        var request = Requests.CreateReadStreamRequest(options);

        return ReadBatchCore(
            request, options.BufferCapacity,
            options.SkipDecoding, options.Filter,
            batchSize, batchTimeout,
            options.CancellationToken
        );
    }

    async ValueTask<Result<RecordStream, ReadError>> ReadCore(
        ReadReq request, int bufferCapacity, bool skipDecoding,
        IRecordFilter filter, CancellationToken cancellationToken
    ) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream), // we cant read it yet we can subscribe to it
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => new UnreachableException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
                };
            }
        }
        catch (RpcException rex) {
            session.Dispose();
            cancellator.Dispose();

            // handle cancellation gracefully
            if (rex.StatusCode == StatusCode.Cancelled)
                return ReadError.FromEx(new OperationCanceledException(rex.Message, rex));

            // when stream is tombstoned an exception is thrown for some weird reason
            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                return ReadError.FromEx(new StreamTombstonedException(stream));
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
                var details  = rex.Message.Split(">>").Last().Trim();
                return ReadError.FromEx(new LogPositionNotFoundException(position, details));
            }

            return ReadError.FromEx(rex);
        }
        catch (Exception ex) {
            session.Dispose();
            cancellator.Dispose();
            return ReadError.FromEx(ex);
        }

        var channelFactory = new Func<Channel<ReadRecordsResult>>(
            () => StartMessageRelay(session, bufferCapacity, skipDecoding, filter, cancellator));

        return new RecordStream(channelFactory);
    }

    async ValueTask<Result<RecordBatchStream, ReadError>> ReadBatchCore(
        ReadReq request, int bufferCapacity, bool skipDecoding,
        IRecordFilter filter, int batchSize, TimeSpan batchTimeout,
        CancellationToken cancellationToken
    ) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => new UnreachableException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
                };
            }
        }
        catch (RpcException rex) {
            session.Dispose();
            cancellator.Dispose();

            if (rex.StatusCode == StatusCode.Cancelled)
                return ReadError.FromEx(new OperationCanceledException(rex.Message, rex));

            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                return ReadError.FromEx(new StreamTombstonedException(stream));
            }

            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
                var details  = rex.Message.Split(">>").Last().Trim();
                return ReadError.FromEx(new LogPositionNotFoundException(position, details));
            }

            return ReadError.FromEx(rex);
        }
        catch (Exception ex) {
            session.Dispose();
            cancellator.Dispose();
            return ReadError.FromEx(ex);
        }

        var channelFactory = new Func<Channel<ReadRecordsResult>>(
            () => StartMessageRelay(session, bufferCapacity, skipDecoding, filter, cancellator));

        return new RecordBatchStream(channelFactory, batchSize, batchTimeout);
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
                    var resp = session.ResponseStream.Current;

                    ReadRecordsResult result;

                    switch (resp?.ContentCase) {
                        case CaughtUp:
                            result = resp.CaughtUp.MapToHeartbeat(sequencer.FetchNext());
                            break;
                        case Event:
                            result = await resp.Event.MapToRecordAsync(
                                sequencer.FetchNext(),
                                SerializerProvider, MetadataDecoder,
                                SchemaRegistryPolicy.NoRequirements,
                                skipDecoding, stoppingToken
                            ).ConfigureAwait(false);
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
