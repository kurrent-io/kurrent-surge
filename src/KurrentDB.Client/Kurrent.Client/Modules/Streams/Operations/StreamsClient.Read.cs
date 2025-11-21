#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;

using System.Threading.Channels;
using Google.Rpc.Exceptions;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using KurrentDB.Protocol.V1.Streams;

using static Kurrent.Client.Streams.StreamsClientV1Mapper;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    /// <summary>
    /// Inspects a record at the specified log position.
    /// </summary>
    /// <param name="position">
    /// The log position to inspect. Must not be <see cref="LogPosition.Unset"/>. Use <see cref="LogPosition.Earliest"/> or <see cref="LogPosition.Latest"/> to inspect the first or last record respectively.
    /// </param>
    /// <param name="skipDecoding">
    /// If true, the record's data and metadata will not be deserialized.
    /// </param>
    /// <param name="cancellationToken">
    /// A cancellation token to observe while waiting for the task to complete.
    /// </param>
    public async ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
        try {
            var request = Requests.CreateInspectRecordRequest(position);

            var messages =  await ReadCore(request, 1, TimeSpan.MaxValue, skipDecoding, RecordFilter.None, cancellationToken)
                .UnwrapAsync().ConfigureAwait(false); ;

            var message = await messages.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (message != ReadMessage.Irrelevant) {
                return message.AsRecord;
            }
            else {
                return Result<Record, InspectRecordError>.Failure(new NotFoundException($"No record found at the specified log position: {position}"));
            }

            // if (message.IsRecord) {
            //     return Result.Success<Record, InspectRecordError>(rec);
            // }
            //     return Result<Record, InspectRecordError>.Failure(new NotFoundException($"No record found at the specified log position: {position}"));


            // var request = position switch {
            //     _ when position == LogPosition.Unset    => throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be unset."),
            //     _ when position == LogPosition.Earliest => Requests.CreateReadLogRequest(ForFirstRecord(cancellationToken)),
            //     _ when position == LogPosition.Latest   => Requests.CreateReadLogRequest(ForLastRecord(cancellationToken)),
            //     _                                       => Requests.CreateInspectRecordRequest(position)
            // };
            //
            // using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);
            //
            // await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
            //
            // if (session.ResponseStream.Current is null)
            //     return Result<Record, InspectRecordError>.Failure(new NotFoundException($"No record found at the specified log position: {position}"));
            //
            // if (session.ResponseStream.Current.ContentCase is not Event)
            //     throw new UnreachableException(
            //         $"Unreachable error while reading with position {position}. " +
            //         $"Response {session.ResponseStream.Current.ContentCase} is unexpected.");
            //
            // return await session.ResponseStream.Current.Event
            //     .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding, cancellationToken)
            //     .ConfigureAwait(false);
        }
        catch (Exception ex) {
            return InspectRecordError.FromEx(ex);
        }
    }

    // public async ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
    //     try {
    //         var request = position switch {
    //             _ when position == LogPosition.Unset    => throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be unset."),
    //             _ when position == LogPosition.Earliest => Requests.CreateReadLogRequest(ForFirstRecord(cancellationToken)),
    //             _ when position == LogPosition.Latest   => Requests.CreateReadLogRequest(ForLastRecord(cancellationToken)),
    //             _                                       => Requests.CreateInspectRecordRequest(position)
    //         };
    //
    //         using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);
    //
    //         await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);
    //
    //         if (session.ResponseStream.Current is null)
    //             return Result<Record, InspectRecordError>.Failure(new NotFoundException($"No record found at the specified log position: {position}"));
    //
    //         if (session.ResponseStream.Current.ContentCase is not Event)
    //             throw new UnreachableException(
    //                 $"Unreachable error while reading with position {position}. " +
    //                 $"Response {session.ResponseStream.Current.ContentCase} is unexpected.");
    //
    //         return await session.ResponseStream.Current.Event
    //             .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding, cancellationToken)
    //             .ConfigureAwait(false);
    //     }
    //     catch (Exception ex) {
    //         return InspectRecordError.FromEx(ex);
    //     }
    //
    //     static ReadOptions ForFirstRecord(CancellationToken ct) =>
    //         new() {
    //             Start             = LogPosition.Earliest,
    //             Direction         = ReadDirection.Forwards,
    //             Limit             = 1,
    //             Filter            = RecordFilter.None,
    //             Heartbeat         = HeartbeatOptions.Disabled,
    //             CancellationToken = ct
    //         };
    //
    //     static ReadOptions ForLastRecord(CancellationToken ct) =>
    //         new() {
    //             Start             = LogPosition.Latest,
    //             Direction         = ReadDirection.Backwards,
    //             Limit             = 1,
    //             Filter            = RecordFilter.None,
    //             Heartbeat         = HeartbeatOptions.Disabled,
    //             CancellationToken = ct
    //         };
    // }

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
        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction);

            using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

            await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);

            if (session.ResponseStream.Current.ContentCase is not StreamNotFound) {
                return await session.ResponseStream.Current.Event
                    .MapToRecordAsync(SerializerProvider, MetadataDecoder, SchemaRegistryPolicy.NoRequirements, skipDecoding, cancellationToken)
                    .ConfigureAwait(false);
            }

            var info = await GetStreamInfo(stream, cancellationToken)
                .UnwrapAsync().ConfigureAwait(false);

            return Result.Failure<Record, ReadError>(info.State switch {
                StreamState.Missing    => new StreamNotFoundException(stream),
                StreamState.Deleted    => new StreamDeletedException(stream),
                StreamState.Tombstoned => new StreamTombstonedException(stream)
            });
        }
        catch (Exception ex) {
            return ReadError.FromEx(ex);
        }
    }

    public ValueTask<Result<Messages, ReadError>> Read(ReadOptions options) {
        var request = Requests.CreateReadLogRequest(options);

        return ReadCore(
            request, options.BufferSize,
            options.Timeout, options.SkipDecoding,
            options.Filter, options.CancellationToken
        );
    }

    public ValueTask<Result<Messages, ReadError>> Read(ReadStreamOptions options) {
        var request = Requests.CreateReadStreamRequest(options);

        return ReadCore(
            request, options.BufferSize,
            options.Timeout, options.SkipDecoding,
            options.Filter, options.CancellationToken
        );
    }

    async ValueTask<Result<Messages, ReadError>> ReadCore(ReadReq request, int bufferSize, TimeSpan timeout, bool skipDecoding, IRecordFilter filter, CancellationToken cancellationToken) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // first message must exist to continue... sigh
            if (session.ResponseStream.Current is null)
                throw new LogPositionNotFoundException(request.Options.All.Position?.CommitPosition ?? LogPosition.Latest);

            // when not found, we must determine if the stream is really missing or deleted
            // tombstoned is a special case that throws FailedPrecondition for some fucking reason
            // just throw so we have a single error path
            if (session.ResponseStream.Current.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Deleted => new StreamDeletedException(stream),
                    StreamState.Missing => new StreamNotFoundException(stream),

                    // meanwhile the stream could have been tombstoned
                    StreamState.Tombstoned => new StreamTombstonedException(stream),

                    // perhaps we just start again? wtf...
                    StreamState.Active => throw new UnreachableException($"Stream '{stream}' reported as not found, but is active.")
                };
            }
        }
        catch (Exception ex) {
            cancellator.Dispose();
            return ReadError.FromEx(ex);
        }

        var channelFactory = new Func<Channel<ReadMessage>>(
            () => StartReadMessageRelay(session, bufferSize, timeout, skipDecoding, filter, cancellator));

        return new Messages(channelFactory);
    }

    // async ValueTask<Result<Messages, ReadError>> ReadCore(ReadReq request, int bufferSize, TimeSpan timeout, bool skipDecoding, IRecordFilter filter, CancellationToken cancellationToken) {
    //     var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    //     var stoppingToken = cancellator.Token;
    //
    //     StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
    //
    //     var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);
    //
    //     try {
    //         await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);
    //
    //         if (session.ResponseStream.Current.ContentCase is StreamNotFound) {
    //             cancellator.Dispose();
    //
    //             // need to determine if the stream is missing, deleted or tombstoned
    //             // StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
    //
    //             // ReSharper disable once PossiblyMistakenUseOfCancellationToken
    //             // --------------------------------------------------------------
    //             // since we are checking the stream details on a failure path,
    //             // we must use the original cancellation token
    //             dynamic error = await GetStreamInfo(stream, cancellationToken).MatchAsync(
    //                 ok => ok.State switch {
    //                     StreamState.Deleted    => new StreamDeletedException(stream), // this cannot be an error right?
    //                     // StreamState.Tombstoned => new StreamTombstonedException(stream), // is a special case for some fucking reason. I hate v1 protocol
    //                     StreamState.Missing    => new StreamNotFoundException(stream)
    //                 },
    //                 ko => ko.AsException
    //             );
    //
    //             // test cause I think that dotnet is not smart enough to match the types here
    //             return Result<Messages, ReadError>.Failure(error);
    //         }
    //     }
    //     catch (Exception ex) {
    //         cancellator.Dispose();
    //         return ReadError.FromEx(ex);
    //     }
    //
    //     // Creates a factory function instead of starting immediately
    //     var channelFactory = new Func<Channel<ReadMessage>>(
    //         () => StartReadMessageRelay(session, bufferSize, timeout, skipDecoding, filter, cancellator));
    //
    //     return new Messages(channelFactory);
    // }

    /// <summary>
    /// Initiates a message relay process to supply messages via a channel.
    /// </summary>
    /// <param name="session">An asynchronous server streaming call providing subscription responses.</param>
    /// <param name="bufferSize">The maximum number of messages the channel can buffer before applying backpressure.</param>
    /// <param name="consumeTimeout">The maximum time to wait for a subscription message before timing out.</param>
    /// <param name="skipDecoding">Whether to skip decoding the record data.</param>
    /// <param name="filter">Optional client-side filter to apply to records.</param>
    /// <param name="cancellator">A cancellation token source used to signal termination of the message relay process.</param>
    /// <returns>A bounded channel that streams subscription messages.</returns>
    Channel<ReadMessage> StartReadMessageRelay(AsyncServerStreamingCall<ReadResp> session, int bufferSize, TimeSpan consumeTimeout, bool skipDecoding, IRecordFilter filter, CancellationTokenSource cancellator) {
        // create bounded channel for backpressure control
        var channel = Channel.CreateBounded<ReadMessage>(
            new BoundedChannelOptions(bufferSize) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            }
        );

        var stoppingToken = cancellator.Token;

        // start pumping messages
        _ = Task.Run(PumpPump, stoppingToken);

        return channel;

        async Task PumpPump() {
            try {
                // must continue from the freaking cursor because we had to try to read
                // the first entry to check if the stream existed or if access was granted
                do {
                    var resp = session.ResponseStream.Current;

                    ReadMessage message = resp.ContentCase switch {
                        Event      => await resp.Event.MapToRecordAsync(SerializerProvider, MetadataDecoder, SchemaRegistryPolicy.NoRequirements, skipDecoding, stoppingToken),
                        Checkpoint => resp.Checkpoint.MapToHeartbeat(),
                        CaughtUp   => resp.CaughtUp.MapToHeartbeat(),
                        FellBehind => resp.FellBehind.MapToHeartbeat(),
                        _          => ReadMessage.Irrelevant
                    };

                    // skip irrelevant messages
                    if (message == ReadMessage.Irrelevant)
                        continue;

                    // apply client-side filter if present
                    if (message.IsRecord && !filter.IsMatch(message))
                        continue;

                    // try to write immediately without blocking
                    if (channel.Writer.TryWrite(message))
                        continue; // no timeout needed

                    // the channel is full, we must add timeout protection
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
                        .With(x => x.CancelAfter(consumeTimeout));

                    await channel.Writer.WriteAsync(message, timeout.Token);
                } while (await session.ResponseStream.MoveNext(stoppingToken));

                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken != stoppingToken) {
                channel.Writer.TryComplete(new TimeoutException($"Timed out after {consumeTimeout}. The application may not be reading messages fast enough."));
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
