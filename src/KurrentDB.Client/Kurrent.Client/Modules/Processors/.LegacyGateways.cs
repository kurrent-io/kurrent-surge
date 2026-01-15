
// DO NOT DELETE
// #pragma warning disable CS8509
// #pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
//
// // ReSharper disable SwitchExpressionHandlesSomeKnownEnumValuesWithExceptionInDefault
//
// using System.Runtime.CompilerServices;
// using System.Threading.Channels;
// using Grpc.Core;
// using Kurrent.Client.Exceptions;
// using Kurrent.Client.Processors.Checkpointing;
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
// using Microsoft.Extensions.Logging;
//
// namespace Kurrent.Client.Streams;
//
// public class LegacyGateway : ClientModuleBase, IProcessorGateway {
//     internal LegacyGateway(KurrentClient client) : base(client) =>
//         StreamsClient = new StreamsClient(client);
//
//     StreamsClient StreamsClient { get; }
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
//         // here we go!
//         var sessionInfo = new WorkerSessionInfo(
//             subscriptionId,
//             settings.WorkerId,
//             settings.ProcessorId
//         );
//
//         var checkpointStore = new WorkerCheckpointStore(
//             processorId: settings.ProcessorId,
//             workerId: settings.WorkerId,
//             maxCount: 10,
//             client: StreamsClient,
//             time: Time,
//             logger: LoggerFactory.CreateLogger<WorkerCheckpointStore>());
//
//         // StartRelay returns channel with unbatched messages
//         var channel = StartRelay(session, settings, cancellator, stoppingToken);
//
//         // CreateBatchedRelay wraps channel with batching
//         var batchedRelay = CreateBatchedRelay(channel, settings, stoppingToken);
//
//         return new WorkerSession(sessionInfo, checkpointStore, batchedRelay, settings);
//
//         // /// <summary>
//         // /// Starts a relay that pumps messages from the gRPC stream into a channel.
//         // /// </summary>
//         // /// <returns>A channel with unbatched raw messages (single Record | CheckpointBarrier).</returns>
//         Channel<RawGatewayMessage> StartRelay(
//             AsyncServerStreamingCall<ReadResp> session,
//             ProcessorSettings options,
//             CancellationTokenSource cancellator,
//             CancellationToken ct
//         ) {
//             // Channel holds unbatched raw messages (single Record | CheckpointBarrier)
//             var channel = Channel.CreateBounded<RawGatewayMessage>(
//                 new BoundedChannelOptions(options.BufferCapacity) {
//                     FullMode     = BoundedChannelFullMode.Wait,
//                     SingleReader = true,
//                     SingleWriter = true
//                 }
//             );
//
//             var stoppingToken = cancellator.Token;
//
//             // start pumping messages
//             _ = Task.Run(PumpPump, stoppingToken);
//
//             // Return just the channel - batching happens in CreateBatchedRelay
//             return channel;
//
//             [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
//             async ValueTask PumpPump() {
//                 SequenceGenerator sequencer = new();
//
//                 try {
//                     // must continue from the freaking cursor because we had to try to read
//                     // the first entry to check if the stream existed or if access was granted
//                     // or if we got a confirmation message
//
//                     do {
//                         var resp = session.ResponseStream.Current;
//
//                         RawGatewayMessage message;
//
//                         switch (resp?.ContentCase) {
//                             case Checkpoint:
//                                 message = resp.Checkpoint.MapToBarrier(sequencer.FetchNext());
//                                 break;
//
//                             case CaughtUp:
//                                 message = resp.CaughtUp.MapToBarrier(sequencer.FetchNext());
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
//         }
//
//         // /// <summary>
//         // /// Wraps a raw message channel with batching logic to produce batched GatewayMessages.
//         // /// </summary>
//         // /// <param name="channel">The channel with unbatched raw messages.</param>
//         // /// <param name="options">Processor settings containing batching configuration.</param>
//         // /// <param name="ct">Cancellation token.</param>
//         // /// <returns>An async enumerable of batched GatewayMessages (Record[] | CheckpointBarrier).</returns>
//         static IAsyncEnumerable<GatewayMessage> CreateBatchedRelay(Channel<RawGatewayMessage> channel, ProcessorSettings options, CancellationToken ct) {
//             return channel.Reader.ReadAllAsync(ct).ReadBatched(
//                 batchSize: options.Batching.MaxSize,
//                 timeout: TimeSpan.FromMilliseconds(options.Batching.Timeout),
//                 classify: ClassifyRawMessage,
//                 batchToOutput: static records => (GatewayMessage)records,
//                 cancellationToken: ct
//             );
//
//             static BatchAction<Record, GatewayMessage> ClassifyRawMessage(RawGatewayMessage msg) =>
//                 msg.IsRecord
//                     ? BatchAction<Record, GatewayMessage>.Batch(msg.AsRecord)
//                     : BatchAction<Record, GatewayMessage>.FlushThenYield(msg.AsCheckpointBarrier);
//         }
//     }
//
//     public ValueTask SendAsync(WorkerMessage message) {
//         return message.SwitchAsync(
//             async workAccepted => {
//                 // log stuff
//             },
//             onCheckpointComplete: async evt => {
//                 // its not really complete until we send this
//                 // so i missed something...
//                 // if we assume that the worker stores its own checkpoints and then just comunicates them here
//                 // maybe it would make sense, but when assigned different partitions the worker would never  know
//                 // what would be the positions to start from unless the gateway told it...
//
//                 // await CheckpointStore
//                 //     .StoreAsync(evt.CheckpointBarrierId, evt.Positions[0], ct)
//                 //     .ConfigureAwait(false);
//             },
//             async disconnect => {
//                 // await CheckpointStore
//                 //     .StoreAsync(disconnect.CheckpointBarrierId, disconnect.Positions[0], ct)
//                 //     .ConfigureAwait(false);
//             }
//         );
//     }
//
//     public ValueTask DisposeAsync() {
//         throw new NotImplementedException();
//     }
// }
//
// record WorkerSession : IWorkerSession {
//     internal WorkerSession(
//         WorkerSessionInfo info,
//         WorkerCheckpointStore checkpointStore,
//         IAsyncEnumerable<GatewayMessage> relay,
//         ProcessorSettings settings
//     ) {
//         SessionInfo = info;
//
//         SessionId       = info.SessionId;
//         WorkerId        = info.WorkerId;
//         ProcessorId     = info.ProcessorId;
//
//         CheckpointStore = checkpointStore;
//         Relay           = relay;
//         Settings        = settings;
//         PositionTracker = new PositionTracker();
//     }
//
//     IAsyncEnumerable<GatewayMessage> Relay           { get; }
//     ProcessorSettings                Settings        { get; }
//     WorkerCheckpointStore            CheckpointStore { get; }
//     PositionTracker                  PositionTracker { get; }
//
//     CheckpointBarrierInfo LastBarrier    { get; set; } = new CheckpointBarrierInfo();
//     CheckpointInfo        LastCheckpoint { get; set; } = new CheckpointInfo();
//
//     public WorkerSessionInfo SessionInfo  { get; }
//
//     public CheckpointState Checkpointing => new() {
//         TrackedPositions = PositionTracker.Positions,
//         LastBarrier      = LastBarrier,
//         LastCheckpoint   = LastCheckpoint
//     };
//
//     public Guid   SessionId   { get; }
//     public string WorkerId    { get; }
//     public string ProcessorId { get; }
//
//     Dictionary<int, PositionTracker> TrackersByPartition { get; } = new();
//
//     // restore checkpoint
//
//
//     // track position
//
//     // store checkpoint
//
//     public ValueTask TrackAsync(RecordPosition position) {
//         TrackersByPartition[position.Partition].Track(position);
//         return ValueTask.CompletedTask;
//     }
//
     // public async IAsyncEnumerator<WorkerSessionMessage> GetAsyncEnumerator(CancellationToken ct = default) {
     //     var workAssigned = AssignWork();
     //
     //     // Initialize one PositionTracker per assigned partition
     //     foreach (var partition in workAssigned.Partitions)
     //         TrackersByPartition.Add(partition, new PositionTracker());
     //
     //     // Yield WorkAssigned as FIRST message (before any records)
     //     yield return workAssigned;
     //
     //     // Pass through batched messages from relay - records are ALREADY batched!
     //     await foreach (var message in Relay.WithCancellation(ct).ConfigureAwait(false)) {
     //         // GatewayMessage: Index 0 = WorkAssigned, Index 1 = Record[], Index 2 = CheckpointBarrier
     //         yield return message.Index switch {
     //             1 => (Record[])message.Value,          // Record[] pass-through
     //             2 => (CheckpointBarrier)message.Value, // CheckpointBarrier pass-through
     //             _ => throw new InvalidOperationException($"Unexpected message type index: {message.Index}")
     //         };
     //     }
     //
     //     yield break;
     //
     //     WorkAssigned AssignWork() {
     //         // Use PartitionAssigner to simulate server behavior
     //         // When multiple workers join, each gets a contiguous subset of partitions
     //         var assignments = PartitionAssigner.Default.AssignPartitions(
     //             workerIds: [WorkerId],
     //             maxParallelism: Settings.MaxParallelism
     //         );
     //
     //         var assignedPartitions = assignments[WorkerId];
     //
     //         return new WorkAssigned(assignedPartitions, PartitionsAssignedReason.Initial);
     //     }
     // }
//
//     public ValueTask SendAsync(WorkerMessage message, CancellationToken ct = default) {
//         return message.SwitchAsync(
//             async workAccepted => {
//                 // log stuff
//             },
//             onCheckpointComplete: async evt => {
//                 // its not really complete until we send this
//                 // so i missed something...
//                 // if we assume that the worker stores its own checkpoints and then just comunicates them here
//                 // maybe it would make sense, but when assigned different partitions the worker would never  know
//                 // what would be the positions to start from unless the gateway told it...
//
//                 await CheckpointStore
//                     .StoreAsync(evt.CheckpointBarrierId, evt.Positions[0], ct)
//                     .ConfigureAwait(false);
//             },
//             async disconnect => {
//                 await CheckpointStore
//                     .StoreAsync(disconnect.CheckpointBarrierId, disconnect.Positions[0], ct)
//                     .ConfigureAwait(false);
//             }
//         );
//     }
//
//
//     public ValueTask DisposeAsync() {
//         throw new NotImplementedException();
//     }
// }
