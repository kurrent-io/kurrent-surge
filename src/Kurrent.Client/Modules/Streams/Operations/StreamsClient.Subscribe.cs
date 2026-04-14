// using System.Runtime.CompilerServices;
// using Grpc.Core;
// using Kurrent.Client.Exceptions;
// using Kurrent.Client.Legacy;
// using Kurrent.Client.Schema;
// using Kurrent.Client.Streams.Checkpoints;
// using Kurrent.Variant;
// using KurrentDB.Protocol.V1.Streams;
//
// namespace Kurrent.Client.Streams;
//
// readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint>;
//
// public readonly record struct Connected(string SessionId);
//
// public readonly record struct Checkpoint(string CheckpointId, CheckpointTrigger Trigger, DateTimeOffset Timestamp);
//
// public enum CheckpointTrigger {
//     /// <summary>
//     /// Unspecified checkpoint trigger.
//     /// </summary>
//     Unspecified = 0,
//
//     /// <summary>
//     /// Checkpoint triggered on rebalancing events to ensure a consistent state across workers.
//     /// </summary>
//     Rebalance = 1,
//
//     /// <summary>
//     /// Time interval elapsed since the last checkpoint.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered automatically by the server based on <see cref="CheckpointSettings.Interval"/>.
//     /// This is the primary mechanism for regular checkpointing.
//     /// </para>
//     /// <para>Frequency: Periodic (every interval milliseconds).</para>
//     /// <para>Auto-deleted: Yes (subject to retention count).</para>
//     /// </remarks>
//     Interval = 2,
//
//     /// <summary>
//     /// Subscription caught up to end of log, transitioning to live mode.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered exactly ONCE per processor lifecycle when the subscription
//     /// finishes processing historical data and transitions to receiving live
//     /// records. Only fires if the subscription started before the stream end
//     /// (i.e., had historical data to process).
//     /// </para>
//     /// <para>
//     /// This ensures that after processing a potentially large historical backlog,
//     /// the state is checkpointed before live processing begins. Prevents replaying
//     /// the entire history if the worker crashes shortly after going live.
//     /// </para>
//     /// <para>Frequency: Once per processor lifecycle (at catch-up completion).</para>
//     /// <para>Auto-deleted: Yes (subject to retention count).</para>
//     /// </remarks>
//     CaughtUp = 4,
//
//     /// <summary>
//     /// Checkpoint triggered manually by the user.
//     /// </summary>
//     /// <remarks>
//     /// <para>
//     /// Triggered by an explicit API call to create a checkpoint, such as
//     /// <c>Processor.CreateCheckpointAsync()</c>. Used for scenarios where
//     /// the application needs to ensure a checkpoint at a specific point
//     /// in processing, outside the automatic mechanisms.
//     /// </para>
//     /// <para>Frequency: Manual (user-initiated).</para>
//     /// <para>Auto-deleted: No (retained until explicitly deleted).</para>
//     /// </remarks>
//     Savepoint = 5,
//
//     /// <summary>
//     /// Checkpoint triggered by graceful worker shutdown to persist final state.
//     /// </summary>
//     Shutdown = 6,
//
//     /// <summary>
//     /// Checkpoint triggered by manually setting a new high watermark position to start from.
//     /// </summary>
//     Reset = 7
// }
//
//
// public partial class StreamsClient {
//     public async IAsyncEnumerable<SubscribeMessage> Subscribe(
//         IRecordFilter filter,
//         RecordPosition start,
//         bool skipDecoding,
//         [EnumeratorCancellation] CancellationToken ct
//     ) {
//         var request = filter.IsStreamFilter
//             ? ProtocolMapping.Requests.CreateStreamSubscriptionRequest(filter, start.Revision)
//             : ProtocolMapping.Requests.CreateLogSubscriptionRequest(filter, start.LogPosition);
//
//         AsyncServerStreamingCall<ReadResp> grpcSession;
//         string sessionId;
//
//         // ── Connection phase ── throws directly to caller, no yield here
//         try {
//             grpcSession = LegacyServiceClient.Read(request, cancellationToken: ct);
//
//             await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);
//
//             // determine the actual stream state when not found (v1 protocol quirk)
//             if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
//                 StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
//                 var info = await GetStreamInfo(stream, ct).ConfigureAwait(false);
//
//                 throw info.State switch {
//                     StreamState.Missing    => new StreamNotFoundException(stream),
//                     StreamState.Tombstoned => new StreamTombstonedException(stream),
//                     _                      => new StreamNotFoundException(stream)
//                 };
//             }
//
//             if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
//                 throw new InvalidOperationException("Expected subscription confirmation as the first message from the server.");
//
//             sessionId = grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId;
//         }
//         catch (RpcException rex) {
//             if (rex.StatusCode == StatusCode.Cancelled)
//                 throw new OperationCanceledException(rex.Message, rex);
//
//             // when stream is tombstoned an exception is thrown for some weird reason
//             if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) { //StatusCode == StatusCode.FailedPrecondition && rex.Trailers.GetValue("exception") == "stream-deleted")) { //rex.Status.Detail.Contains("is deleted")
//                 StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
//                 throw new StreamTombstonedException(stream);
//             }
//
//             // when a position does not exist (targets a position between records in the same stream)
//             if (rex.StatusCode == StatusCode.Unknown && rex.Message.Contains("InvalidPosition >>", StringComparison.OrdinalIgnoreCase)) {
//                 var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Latest;
//                 var details  = rex.Message.Split(">>").Last().Trim();
//                 throw new LogPositionNotFoundException(position, details);
//
//                 throw new LogPositionNotFoundException(
//                     request.Options.All.Position?.CommitPosition
//                  ?? LogPosition.Unset, details);
//             }
//
//             throw rex.AsWellKnownException();
//         }
//
//         yield return new Connected(sessionId);
//
//         // ── Pump phase ── yields Record | Checkpoint
//         SequenceGenerator sequencer = new();
//
//         try {
//             while (await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false)) {
//                 var resp = grpcSession.ResponseStream.Current;
//
//                 switch (resp?.ContentCase) {
//                     case ReadResp.ContentOneofCase.Checkpoint:
//                         yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext());
//                         break;
//
//                     case ReadResp.ContentOneofCase.CaughtUp:
//                         yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
//                         break;
//
//                     case ReadResp.ContentOneofCase.Event:
//                         var record = await resp.Event.MapToRecordAsync(
//                             sequencer.FetchNext(),
//                             SerializerProvider, MetadataDecoder,
//                             SchemaRegistryPolicy.NoRequirements,
//                             skipDecoding,
//                             ct
//                         ).ConfigureAwait(false);
//
//                         // client-side filter until v2 protocol has a proper Subscription operation
//                         if (filter.IsMatch(record))
//                             yield return record;
//
//                         break;
//                 }
//             }
//         }
//         finally {
//             grpcSession.Dispose();
//         }
//     }
// }
