#pragma warning disable CS8509
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.

// ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Schema;
using KurrentDB.Protocol.V1.Streams;

using static System.StringComparison;
using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;

using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Processors.Checkpointing;
using Kurrent.Processors.Gateway;
using Kurrent.Processors.Models;
using Kurrent.Variant;

namespace Kurrent.Client.Streams;

public class LegacyGateway : ClientModuleBase, IProcessorGateway {
    internal LegacyGateway(KurrentClient client) : base(client) =>
        StreamsClient = new StreamsClient(client);

    StreamsClient StreamsClient { get; }

    public async ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct) {
        var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var stoppingToken = cancellator.Token;

        var request = ProtocolMapping.Requests.CreateSubscriptionRequest(settings);

        AsyncServerStreamingCall<ReadResp>? session;


        var sessionId = SessionId.None;

        try {
            session = StreamsClient.LegacyServiceClient.Read(request, cancellationToken: stoppingToken);

            await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);

            // determine the actual stream state when not found (v1 protocol quirk)
            if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await StreamsClient.GetStreamInfo(stream, stoppingToken)
                    .UnwrapAsync().ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing => new StreamNotFoundException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    _ => throw new InvalidOperationException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
                };
            }

            sessionId = session.ResponseStream.Current?.ContentCase is Confirmation
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

        // StartRelay returns channel with unbatched messages
        var channel = StartRelay(session, settings, stoppingToken);

        // Wrap in LegacyWorkerSession which handles batching internally
        // Session owns cancel (triggers pump exit), pump owns final dispose
        return new LegacyWorkerSession(
            sessionId, channel, settings,
            HandleWorkerMessage, cancellator
        );

        // Starts a relay that pumps messages from the gRPC stream into a channel.
        // Returns a channel with unbatched raw messages (single Record | CheckpointBarrier).
        Channel<LegacyGatewayMessage> StartRelay(
            AsyncServerStreamingCall<ReadResp> grpcSession,
            ProcessorSettings options,
            CancellationToken stoppingToken
        ) {
            // Channel holds unbatched raw messages (single Record | CheckpointBarrier)
            // SingleWriter = false: pump + timer both write
            var channel = Channel.CreateBounded<LegacyGatewayMessage>(
                new BoundedChannelOptions(options.Worker.BufferCapacity) {
                    FullMode     = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                }
            );

            // start pumping messages
            _ = Task.Run(PumpPump, stoppingToken);

            // start barrier timer
            _ = Task.Run(BarrierTimerAsync, stoppingToken);

            return channel;

            [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
            async ValueTask PumpPump() {
                var recordFilter = options.Subscription.Filter;

                SequenceGenerator sequencer = new(); // remove this.

                try {
                    // must continue from the freaking cursor because we had to try to read
                    // the first entry to check if the stream existed or if access was granted
                    // or if we got a confirmation message

                    do {
                        var resp = grpcSession.ResponseStream.Current;

                        LegacyGatewayMessage message;

                        switch (resp?.ContentCase) {
                            case Checkpoint:
                                message = resp.Checkpoint.MapToBarrier(sequencer.FetchNext());
                                break;

                            case CaughtUp:
                                message = resp.CaughtUp.MapToBarrier(sequencer.FetchNext());
                                break;

                            case Event:
                                // Slow path: requires async for metadata decoding at least
                                var record = await resp.Event.MapToRecordAsync(
                                    sequencer.FetchNext(),
                                    SerializerProvider, MetadataDecoder,
                                    SchemaRegistryPolicy.NoRequirements,
                                    options.Worker.SkipDecoding,
                                    stoppingToken
                                ).ConfigureAwait(false);

                                // calculate partition mimicking server behavior
                                // next: in v2 the partition will be sent with the new record
                                var partition = PartitionCalculator.XXHash
                                    .Calculate(record.Stream, options.MaxParallelism);

                                // set partition on the record's FullPosition using `with` expression
                                message = record with {
                                    Position = record.Position with {
                                        Partition = Partition.From(partition)
                                    }
                                };

                                if (recordFilter.IsMatch(record))
                                    break;
                                else
                                    continue;

                            // Skip everything else
                            // FellBehind, FirstStreamPosition, LastStreamPosition, LastAllStreamPosition, None and null
                            default:
                                continue;
                        }

                        // // apply client-side filter if present
                        // if (message.IsRecord && !recordFilter.IsMatch(message.AsRecord))
                        //     continue;

                        await channel.Writer
                            .WriteAsync(message, stoppingToken)
                            .ConfigureAwait(false);

                    } while (await grpcSession.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));

                    channel.Writer.TryComplete();
                }
                // if we are to use the ShutdownReason.Cancelled path, we must not swallow RpcException cancellations
                // and we must convert them to OperationCanceledExceptions
                catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
                    // channel.Writer.TryComplete(new OperationCanceledException(rex.Message));
                    channel.Writer.TryComplete();
                }
                // if we are to use the ShutdownReason.Cancelled path, we must not swallow OperationCanceledExceptions
                catch (OperationCanceledException) {
                    channel.Writer.TryComplete();
                }
                catch (Exception ex) {
                    channel.Writer.TryComplete(ex.ToException());
                }
                finally {
                    grpcSession.Dispose();
                }
            }

            async ValueTask BarrierTimerAsync() {
                var clock = new HybridLogicalClock(Time);

                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Checkpoint.Interval));

                try {
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) {

                        var now = clock.Now();

                        var barrier = new CheckpointBarrier(
                            BarrierId.From(now),
                            CheckpointTrigger.Interval,
                            now.DateTimeOffset // interesting right? -_-'
                        );

                        await channel.Writer
                            .WriteAsync(barrier, stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    // Expected on shutdown
                }
            }
        }
    }

    /// <summary>
    /// Handles outbound worker messages. Passed to session as delegate.
    /// </summary>
    ValueTask HandleWorkerMessage(WorkerMessage message) {
        // TODO: we should log on all these messages.

        return message.SwitchAsync(
            onConnect:            _ => ValueTask.CompletedTask,
            onWorkerReady:        _ => ValueTask.CompletedTask,
            onCheckpointComplete: _ => ValueTask.CompletedTask,
            onDisconnect:         _ => ValueTask.CompletedTask
        );
    }
}

readonly partial record struct LegacyGatewayMessage : IVariant<Record, CheckpointBarrier>;
