#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Diagnostics;

using System.Threading.Channels;
using Google.Rpc.Exceptions;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using KurrentDB.Protocol.V1.Streams;

using static Kurrent.Client.Streams.StreamsClientV1Mapper;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    public async ValueTask<Result<Record, InspectRecordError>> InspectRecord(LogPosition position, bool skipDecoding = true, CancellationToken cancellationToken = default) {
        try {
            var request = position switch {
                _ when position == LogPosition.Unset    => throw new ArgumentOutOfRangeException(nameof(position), "Log position cannot be unset."),
                _ when position == LogPosition.Earliest => Requests.CreateReadRequest(ReadAllOptions.FirstRecord(cancellationToken)),
                _ when position == LogPosition.Latest   => Requests.CreateReadRequest(ReadAllOptions.LastRecord(cancellationToken)),
                _                                       => Requests.CreateInspectRecordRequest(position)
            };

            using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

            await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);

            if (session.ResponseStream.Current is null)
                return Result<Record, InspectRecordError>.Failure(new NotFoundException($"No record found at the specified log position: {position}"));

            if (session.ResponseStream.Current.ContentCase is not Event)
                throw new UnreachableException(
                    $"Unreachable error while reading with position {position}. " +
                    $"Response {session.ResponseStream.Current.ContentCase} is unexpected.");

            return await session.ResponseStream.Current.Event
                .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            return InspectRecordError.FromEx(ex);
        }
    }

    public async ValueTask<Result<Record, ReadError>> ReadStreamEdge(StreamName stream, ReadDirection direction, bool skipDecoding, CancellationToken cancellationToken) {
        try {
            var request = Requests.CreateReadStreamEdgeRequest(stream, direction);

            using var session = LegacyServiceClient.Read(request, cancellationToken: cancellationToken);

            await session.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false);

            if (session.ResponseStream.Current.ContentCase is not StreamNotFound) {
                return await session.ResponseStream.Current.Event
                    .MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding, cancellationToken)
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

    public ValueTask<Result<Messages, ReadError>> ReadStream(StreamName stream, CancellationToken cancellationToken = default) =>
        ReadStream(new ReadStreamOptions { Stream = stream, CancellationToken = cancellationToken });

    public ValueTask<Result<Messages, ReadError>> ReadAll(ReadAllOptions options) =>
        ReadCore(Requests.CreateReadRequest(options), options.BufferSize, options.Timeout, options.SkipDecoding, options.CancellationToken);

    public ValueTask<Result<Messages, ReadError>> ReadStream(ReadStreamOptions options) =>
        ReadCore(Requests.CreateReadRequest(options), options.BufferSize, options.Timeout, options.SkipDecoding, options.CancellationToken);

    async ValueTask<Result<Messages, ReadError>> ReadCore(ReadReq request, int bufferSize, TimeSpan timeout, bool skipDecoding, CancellationToken cancellationToken) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            if (session.ResponseStream.Current.ContentCase is StreamNotFound) {
                cancellator.Dispose();

                // need to determine if the stream is missing, deleted or tombstoned
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                // --------------------------------------------------------------
                // since we are checking the stream details on a failure path,
                // we must use the original cancellation token
                var error = await GetStreamInfo(stream, cancellationToken).MatchAsync(
                    ok => ok.State switch {
                        StreamState.Deleted    => new StreamDeletedException(stream), // this cannot be an error right?
                        StreamState.Tombstoned => new StreamTombstonedException(stream),
                        StreamState.Missing    => new StreamNotFoundException(stream)
                    },
                    ko => ko.AsException
                );

                // test cause I think that dotnet is not smart enough to match the types here
                return Result<Messages, ReadError>.Failure(error);
            }
        }
        catch (Exception ex) {
            cancellator.Dispose();
            return ReadError.FromEx(ex);
        }

        // Creates a factory function instead of starting immediately
        var channelFactory = new Func<Channel<ReadMessage>>(() => StartReadMessageRelay(session, bufferSize, timeout, skipDecoding, cancellator));

        return new Messages(channelFactory);
    }

    /// <summary>
    /// Initiates a message relay process to supply messages via a channel.
    /// </summary>
    /// <param name="session">An asynchronous server streaming call providing subscription responses.</param>
    /// <param name="bufferSize">The maximum number of messages the channel can buffer before applying backpressure.</param>
    /// <param name="consumeTimeout">The maximum time to wait for a subscription message before timing out.</param>
    /// <param name="skipDecoding"></param>
    /// <param name="cancellator">A cancellation token source used to signal termination of the message relay process.</param>
    /// <returns>A bounded channel that streams subscription messages.</returns>
    Channel<ReadMessage> StartReadMessageRelay(AsyncServerStreamingCall<ReadResp> session, int bufferSize, TimeSpan consumeTimeout, bool skipDecoding, CancellationTokenSource cancellator) {
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

        async Task PumpPump() {
            try {
                // must continue from the freaking cursor because we had to try to read
                // the first entry to check if the stream existed or if access was granted
                do {
                    var resp = session.ResponseStream.Current;

                    if (resp.ContentCase is not (Event or Checkpoint or CaughtUp or FellBehind)) continue;

                    ReadMessage message = resp.ContentCase switch {
                        Event      => await resp.Event.MapToRecordAsync(SerializerProvider, MetadataDecoder, skipDecoding, stoppingToken).ConfigureAwait(false),
                        Checkpoint => resp.Checkpoint.MapToHeartbeat(),
                        CaughtUp   => resp.CaughtUp.MapToHeartbeat(),
                        FellBehind => resp.FellBehind.MapToHeartbeat(),
                        _          => throw new InvalidOperationException($"Unreachable error while reading stream. {resp.ContentCase}")
                    };

                    // try to write immediately without blocking
                    if (channel.Writer.TryWrite(message))
                        continue; // no timeout needed

                    // the channel is full, we must add timeout protection
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
                        .With(x => x.CancelAfter(consumeTimeout));

                    await channel.Writer
                        .WriteAsync(message, timeout.Token)
                        .ConfigureAwait(false);
                } while (await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));

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

        return channel;
    }
}
