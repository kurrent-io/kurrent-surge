#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Schema;
using KurrentDB.Protocol.V1.Streams;

using static System.StringComparison;
using static Kurrent.Client.Streams.StreamsClientV1Mapper;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

namespace Kurrent.Client.Streams;

public partial class StreamsClient {
    internal async ValueTask<Result<ProcessRecordBatchStream, ProcessError>> Consume(ProcessOptions options, CancellationToken cancellationToken = default) {
        var request = Requests.CreateSubscriptionRequest(options);

        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        var subscriptionId = Guid.Empty;

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => throw new InvalidOperationException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
                };
            }

            subscriptionId = session.ResponseStream.Current?.ContentCase is Confirmation
                ? Guid.Parse(session.ResponseStream.Current!.Confirmation.SubscriptionId)
                : throw new InvalidOperationException("Expected subscription confirmation as the first message from the server.");
        }
        catch (RpcException rex) {
            cancellator.Dispose();

            // handle cancellation gracefully
            if (rex.StatusCode == StatusCode.Cancelled)
                return ProcessError.FromEx(new OperationCanceledException(rex.Message, rex));

            // when stream is tombstoned an exception is thrown for some weird reason
            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                return ProcessError.FromEx(new StreamTombstonedException(stream));
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                // StatusCode="Unknown", Detail="
                //   Unexpected ReadAllResult: InvalidPosition >>
                //   There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
                //   Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
                // "

                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
                var details  = rex.Message.Split(">>").Last().Trim();
                return ProcessError.FromEx(new LogPositionNotFoundException(position, details));
            }

            return ProcessError.FromEx(rex);
        }
        catch (Exception ex) {
            cancellator.Dispose();
            return ProcessError.FromEx(ex);
        }

        var channelFactory = new Func<Channel<ReadRecordsResult>>(() => StartRelay(session, options, cancellator));

        return new ProcessRecordBatchStream(channelFactory, options.Batching.Size, TimeSpan.FromMilliseconds(options.Batching.Timeout), subscriptionId);
    }

    Channel<ReadRecordsResult> StartRelay(AsyncServerStreamingCall<ReadResp> session, ProcessOptions options, CancellationTokenSource cancellator) {
        var channel = Channel.CreateBounded<ReadRecordsResult>(
            new BoundedChannelOptions(options.BufferCapacity) {
                FullMode     = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            }
        );

        var stoppingToken = cancellator.Token;

        // start pumping messages
        _ = Task.Run(PumpPump, stoppingToken);

        return channel;

        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
        async ValueTask PumpPump() {
            try {
                // must continue from the freaking cursor because we had to try to read
                // the first entry to check if the stream existed or if access was granted
                // or if we got a confirmation message

                do {
                    var resp = session.ResponseStream.Current;

                    ReadRecordsResult result;

                    switch (resp?.ContentCase) {
                        case Checkpoint:
                            result = resp.Checkpoint.MapToHeartbeat();
                            break;
                        case CaughtUp:
                            result = resp.CaughtUp.MapToHeartbeat();
                            break;
                        case FellBehind:
                            result = resp.FellBehind.MapToHeartbeat();
                            break;
                        case Event:
                            // Slow path: requires async
                            result = await resp.Event.MapToRecordAsync(
                                SerializerProvider, MetadataDecoder,
                                SchemaRegistryPolicy.NoRequirements,
                                options.SkipDecoding, stoppingToken
                            ).ConfigureAwait(false);
                            break;

                        case None:
                        case FirstStreamPosition:
                        case LastStreamPosition:
                        case LastAllStreamPosition:
                        case null:
                            continue;

                        default:
                            continue; // Skip confirmation or irrelevant, no allocation
                    }

                    // apply client-side filter if present
                    if (result.IsRecord && !options.Filter.IsMatch(result))
                        continue;

                    await channel.Writer.WriteAsync(result, stoppingToken).ConfigureAwait(false);
                } while (await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));

                channel.Writer.TryComplete();
            }
            // if we are to use the ShutdownReason.Cancelled path, we must not swallow RpcException cancellations
            // and we must convert them to OperationCanceledExceptions
            catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
                channel.Writer.TryComplete();
            }
            // if we are to use the ShutdownReason.Cancelled path, we must not swallow OperationCanceledExceptions
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


    internal async ValueTask<ProcessRecordBatchStream> ConsumeAsync(ProcessOptions options, CancellationToken cancellationToken = default) {
        var request = Requests.CreateSubscriptionRequest(options);

        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppingToken = cancellator.Token;

        var session = LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

        var subscriptionId = Guid.Empty;

        try {
            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _                      => throw new InvalidOperationException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
                };
            }

            subscriptionId = session.ResponseStream.Current?.ContentCase is Confirmation
                ? Guid.Parse(session.ResponseStream.Current!.Confirmation.SubscriptionId)
                : throw new InvalidOperationException("Expected subscription confirmation as the first message from the server.");
        }
        catch (RpcException rex) {
            cancellator.Dispose();

            // handle cancellation gracefully
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            // when stream is tombstoned an exception is thrown for some weird reason
            if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
                // StatusCode="Unknown", Detail="
                //   Unexpected ReadAllResult: InvalidPosition >>
                //   There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
                //   Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
                // "

                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
                var details  = rex.Message.Split(">>").Last().Trim();

                throw new LogPositionNotFoundException(position, details);
            }

            throw rex.ToException();
        }
        catch (Exception ex) {
            cancellator.Dispose();
            throw ex.ToException();
        }

        var channelFactory = new Func<Channel<ReadRecordsResult>>(() => StartRelay(session, options, cancellator));

        return new ProcessRecordBatchStream(channelFactory, options.Batching.Size, TimeSpan.FromMilliseconds(options.Batching.Timeout), subscriptionId);
    }
}
