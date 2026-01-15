// #pragma warning disable CS8509
// #pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
//
// // ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
//
// using System.Runtime.CompilerServices;
// using System.Threading.Channels;
// using Grpc.Core;
// using Kurrent.Client.Exceptions;
// using Kurrent.Client.Processors.Messages;
// using Kurrent.Client.Processors.Relay;
// using Kurrent.Client.Schema;
// using KurrentDB.Protocol.V1.Streams;
//
// using static System.StringComparison;
// using static Kurrent.Client.Streams.StreamsClientV1Mapper;
// using static KurrentDB.Protocol.V1.Streams.ReadResp.ContentOneofCase;
//
// using Kurrent.Client.Processors.Partitioning;
// using Kurrent.Client.Streams.Checkpoints;
// using Kurrent.Client.Streams.Processing;
// using Kurrent.Variant;
//
// namespace Kurrent.Client.Streams;
//
// public class LegacyGateway : ClientModuleBase, IProcessorGateway {
//     internal LegacyGateway(KurrentClient client) : base(client) =>
//         StreamsClient = new StreamsClient(client);
//
//     StreamsClient StreamsClient { get; }
//
//     TaskCompletionSource?  _activePendingAck;
//
//     public async ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct) {
//         var cancellator   = CancellationTokenSource.CreateLinkedTokenSource(ct);
//         var stoppingToken = cancellator.Token;
//
//         var request = Requests.CreateSubscriptionRequest(settings);
//         var session = StreamsClient.LegacyServiceClient.Read(request, cancellationToken: stoppingToken);
//
//         Guid subscriptionId;
//
//         try {
//             await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false);
//
//             // determine the actual stream state when not found (v1 protocol quirk)
//             if (session.ResponseStream.Current?.ContentCase is StreamNotFound) {
//                 StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
//
//                 var info = await StreamsClient.GetStreamInfo(stream, stoppingToken)
//                     .UnwrapAsync().ConfigureAwait(false);
//
//                 throw info.State switch {
//                     StreamState.Missing => new StreamNotFoundException(stream),
//                     StreamState.Tombstoned => new StreamTombstonedException(stream),
//                     _ => throw new InvalidOperationException($"Stream '{stream}' reported as not found, but has unexpected state '{info.State}'.")
//                 };
//             }
//
//             subscriptionId = session.ResponseStream.Current?.ContentCase is Confirmation
//                 ? Guid.Parse(session.ResponseStream.Current!.Confirmation.SubscriptionId)
//                 : throw new InvalidOperationException("Expected subscription confirmation as the first message from the server.");
//         }
//         catch (RpcException rex) {
//             cancellator.Dispose();
//
//             // handle cancellation gracefully
//             if (rex.StatusCode == StatusCode.Cancelled)
//                 throw new OperationCanceledException(rex.Message, rex);
//
//             // when stream is tombstoned an exception is thrown for some weird reason
//             if (rex.StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted") {
//                 StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
//                 throw new StreamTombstonedException(stream);
//             }
//
//             // when a position does not exist (targets a position between records in the same stream)
//             if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", OrdinalIgnoreCase)) {
//                 // StatusCode="Unknown", Detail="
//                 //   Unexpected ReadAllResult: InvalidPosition >>
//                 //   There is not enough space to read full record (length prefix: 143170). Actual pre-position: 144798632.
//                 //   Something is seriously wrong in chunk #1-1 (chunk-000001.000000).
//                 // "
//
//                 var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
//                 var details  = rex.Message.Split(">>").Last().Trim();
//
//                 throw new LogPositionNotFoundException(position, details);
//             }
//
//             throw rex.ToException();
//         }
//         catch (Exception ex) {
//             cancellator.Dispose();
//             throw ex.ToException();
//         }
//
//         // StartRelay returns channel with unbatched messages
//         var channel = StartRelay(session, settings, cancellator, stoppingToken);
//
//         // Wrap in LegacyWorkerSession which handles batching internally for simplicity
//         return new LegacyWorkerSession(subscriptionId, channel, settings.Batching);
//
//         // /// <summary>
//         // /// Starts a relay that pumps messages from the gRPC stream into a channel.
//         // /// </summary>
//         // /// <returns>A channel with unbatched raw messages (single Record | CheckpointBarrier).</returns>
//         Channel<LegacyGatewayMessage> StartRelay(
//             AsyncServerStreamingCall<ReadResp> session,
//             ProcessorSettings options,
//             CancellationTokenSource cancellator,
//             CancellationToken ct
//         ) {
//             // Channel holds unbatched raw messages (single Record | CheckpointBarrier)
//             // SingleWriter = false: pump + timer both write
//             var channel = Channel.CreateBounded<LegacyGatewayMessage>(
//                 new BoundedChannelOptions(options.BufferCapacity) {
//                     FullMode     = BoundedChannelFullMode.Wait,
//                     SingleReader = true,
//                     SingleWriter = false
//                 }
//             );
//
//             var stoppingToken = cancellator.Token;
//
//             // Shared state for timer to read last position
//             var lastPosition = LogPosition.Unset;
//
//             // start pumping messages
//             _ = Task.Run(PumpPump, stoppingToken);
//
//             // Start interval barrier timer if auto-commit is enabled
//             if (options.Checkpoint is { AutoCommitEnabled: true, Interval: > 0 }) {
//                 _ = Task.Run(BarrierTimerAsync, stoppingToken);
//             }
//
//             return channel;
//
//             [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
//             async ValueTask PumpPump() {
//                 SequenceGenerator sequencer = new();
//                 var recordsSinceBarrier = 0;
//
//                 try {
//                     // must continue from the freaking cursor because we had to try to read
//                     // the first entry to check if the stream existed or if access was granted
//                     // or if we got a confirmation message
//
//                     do {
//                         var resp = session.ResponseStream.Current;
//
//                         LegacyGatewayMessage message;
//
//                         switch (resp?.ContentCase) {
//                             case Checkpoint:
//                                 message = resp.Checkpoint.MapToBarrier(sequencer.FetchNext());
//                                 recordsSinceBarrier = 0; // Reset counter on server barrier
//                                 break;
//
//                             case CaughtUp:
//                                 message = resp.CaughtUp.MapToBarrier(sequencer.FetchNext());
//                                 recordsSinceBarrier = 0; // Reset counter on server barrier
//                                 break;
//
//                             case Event:
//                                 // Slow path: requires async
//                                 var record = await resp.Event.MapToRecordAsync(
//                                     sequencer.FetchNext(),
//                                     SerializerProvider, MetadataDecoder,
//                                     SchemaRegistryPolicy.NoRequirements,
//                                     options.SkipDecoding,
//                                     stoppingToken
//                                 ).ConfigureAwait(false);
//
//                                 // Calculate partition using PartitionCalculator (simulating server behavior)
//                                 // The real server would determine partition before sending to worker
//                                 var partition = PartitionCalculator.Default.For(record.Stream, options.MaxParallelism);
//
//                                 // Set partition on the record's FullPosition using `with` expression
//                                 message = record with {
//                                     FullPosition = record.FullPosition with {
//                                         Partition = Partition.From(partition)
//                                     }
//                                 };
//
//                                 break;
//
//                             case FellBehind:
//                             case None:
//                             case FirstStreamPosition:
//                             case LastStreamPosition:
//                             case LastAllStreamPosition:
//                             case null:
//                                 continue;
//
//                             default:
//                                 continue; // Skip confirmation or irrelevant, no allocation
//                         }
//
//                         // apply client-side filter if present
//                         if (message.IsRecord && !options.Subscription.Filter.IsMatch(message.AsRecord))
//                             continue;
//
//                         await channel.Writer.WriteAsync(message, stoppingToken).ConfigureAwait(false);
//
//                         // Update shared position for timer and check threshold
//                         if (message.IsRecord) {
//                             var recordPosition = message.AsRecord.FullPosition.LogPosition;
//                             lastPosition = recordPosition;
//                             recordsSinceBarrier++;
//
//                             // Threshold barrier check
//                             if (options.Checkpoint.AutoCommitEnabled &&
//                                 options.Checkpoint.Threshold > 0 &&
//                                 recordsSinceBarrier >= options.Checkpoint.Threshold) {
//
//                                 var barrier = new CheckpointBarrier(
//                                     recordPosition,
//                                     CheckpointBarrierTrigger.Threshold,
//                                     Time.GetUtcNow()
//                                 );
//                                 await channel.Writer.WriteAsync(barrier, stoppingToken).ConfigureAwait(false);
//                                 recordsSinceBarrier = 0;
//                             }
//                         }
//
//                     } while (await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));
//
//                     channel.Writer.TryComplete();
//                 }
//                 // if we are to use the ShutdownReason.Cancelled path, we must not swallow RpcException cancellations
//                 // and we must convert them to OperationCanceledExceptions
//                 catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
//                     channel.Writer.TryComplete();
//                 }
//                 // if we are to use the ShutdownReason.Cancelled path, we must not swallow OperationCanceledExceptions
//                 catch (OperationCanceledException) {
//                     channel.Writer.TryComplete();
//                 }
//                 catch (Exception ex) {
//                     channel.Writer.TryComplete(ex);
//                 }
//                 finally {
//                     session.Dispose();
//                     cancellator.Dispose();
//                 }
//             }
//
//             async ValueTask BarrierTimerAsync() {
//                 using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(options.Checkpoint.Interval));
//
//                 try {
//                     while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) {
//                         var position = lastPosition;
//                         if (position.IsUnset)
//                             continue; // Skip if no records yet
//
//                         var barrier = new CheckpointBarrier(
//                             position,
//                             CheckpointBarrierTrigger.Interval,
//                             Time.GetUtcNow()
//                         );
//
//                         await channel.Writer.WriteAsync((LegacyGatewayMessage)barrier, stoppingToken).ConfigureAwait(false);
//                     }
//                 }
//                 catch (OperationCanceledException) {
//                     // Expected on shutdown
//                 }
//             }
//         }
//
//
//                 try {
//                     do {
//                         var resp = session.ResponseStream.Current;
//                         // process response...
//                     } while (await session.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false));
//
//                     channel.Writer.TryComplete();
//                 }
//                 catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
//                     channel.Writer.TryComplete();
//                 }
//                 catch (OperationCanceledException) {
//                     channel.Writer.TryComplete();
//                 }
//                 catch (Exception ex) {
//                     channel.Writer.TryComplete(ex);
//                 }
//                 finally {
//                     session.Dispose();
//                     cancellator.Dispose();
//                 }
//     }
//
//     public ValueTask SendAsync(WorkerMessage message) {
//         // we should log on all these messages.
//
//         return message.SwitchAsync(
//             connect => ValueTask.CompletedTask, // No-op for Connect
//             workerReady => {
//                 // Unblock pump waiting for WorkAccepted
//                 _activePendingAck?.TrySetResult();
//                 return ValueTask.CompletedTask;
//             },
//             async checkpointComplete => {
//                 // Unblock pump waiting for checkpoint ack
//                 _activePendingAck?.TrySetResult();
//             },
//             async disconnect => {
//                 // Currently no-op - server handles disconnect when stream is disposed
//             }
//         );
//     }
//
//     internal void SetPendingAck(TaskCompletionSource? pendingAck) => _activePendingAck = pendingAck;
//
//     public ValueTask DisposeAsync() {
//         throw new NotImplementedException();
//     }
// }
//
// readonly partial record struct LegacyGatewayMessage : IVariant<Record, CheckpointBarrier>;
