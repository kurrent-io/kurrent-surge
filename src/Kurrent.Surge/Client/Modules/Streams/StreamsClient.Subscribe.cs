using Kurrent.Toolkit.Grpc;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Surge.Client.Exceptions;
using Kurrent.Surge.Client.Streams.Checkpoints;
using Kurrent.Surge.Client.Legacy;
using KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Surge.Client.Streams;

public partial class StreamsClient {
    public async IAsyncEnumerable<SubscribeMessage> Subscribe(
        SubscribeOptions options,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var start = options.Start;

        // Same target routing as reads: a stream-name filter targets a single stream, whose
        // wire currency is a revision. A start whose stream half doesn't name THIS target
        // (e.g. a $all checkpoint pointing at a foreign record) is never trusted — its log
        // half is resolved to the last processed record of the target instead, so the
        // exclusive After(...) resume is exact.
        if (options.Filter.UnwrapFilter() is StreamNameFilter target) {
            // Sentinels speak both currencies: pin them to the target so the wire maps them
            // natively (Earliest → first revision, Latest → end of stream).
            start =
                start.LogPosition == LogPosition.Earliest ? RecordPosition.From(LogPosition.Earliest, target.TargetStream, Revision.Min)
                : start.LogPosition == LogPosition.Latest ? RecordPosition.From(LogPosition.Latest, target.TargetStream, Revision.Max)
                : start;

            // bug? start.Stream is now target.TargetStream cause the sentinel alignment above just overrode the variable
            if (start.Stream != target.TargetStream || start.Revision == Revision.Unset) {
                if (start.LogPosition == LogPosition.Unset)
                    throw new ArgumentException(
                        "Start position cannot address the target stream: it carries neither a " +
                        "matching stream revision nor a log position to resolve one from.", nameof(options));

                var boundary = await ResolveSubscriptionStart(RecordPosition.From(start.LogPosition, target.TargetStream, Revision.Unset), ct)
                    .ConfigureAwait(false);

                // None = nothing of the stream processed yet → subscribe from the start of
                // the stream (deliver everything), NOT an empty subscription.
                start = boundary.IsUnset
                    ? RecordPosition.From(LogPosition.Unset, target.TargetStream, Revision.Min)
                    : boundary;
            }
        }
        else if (start.LogPosition == LogPosition.Unset) {
            // The log is addressed by position only — never guess from a stream half.
            throw new ArgumentException("Log-targeted subscriptions require a start with a log position.", nameof(options));
        }

        var request = StreamsClientMapping.Requests.CreateSubscriptionRequest(
            options.Filter, start, options.ResolveLinks, options.MaxSearchWindow);

        AsyncServerStreamingCall<ReadResp> grpcSession;
        SessionId                          sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = LegacyReadServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"EVERYTHING IS BROKEN! First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}");

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                throw new LogPositionNotFoundException(position, rex);
            }

            throw rex.AsWellKnownException();
        }

        yield return new Connected(sessionId);

        SequenceGenerator sequencer = new();

        // The filter is the authority on whether the SDK evaluates it client-side. When it does,
        // it discards records the consumer never sees — invisible progress — so this subscription
        // must raise scan checkpoints of its own, exactly as a server-side filtered subscription
        // would. A purely server-routable filter leaves this off: the server delivers only matches
        // and (on a filtered $all) raises its own checkpoints.
        var filtersLocally = options.Filter.RequiresClientEvaluation;

        // Records received since the last checkpoint of any kind. A server checkpoint (filter or
        // caught-up) resets it: progress is already accounted for up to the server's watermark, so
        // the count restarts from there — "count locally since the last server-sent one".
        uint scannedSinceCheckpoint = 0;

        try {
            while (await grpcSession.ResponseStream.MoveNextSafe(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint:
                        // Server watermark — covers progress up to here, so reset the local window.
                        scannedSinceCheckpoint = 0;
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.CaughtUp: {
                        scannedSinceCheckpoint = 0;

                        var checkpoint = resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                        var position   = checkpoint.Positions[0];

                        // A stream-subscription CaughtUp carries the stream revision but no log position: it
                        // marks the live end, where no record exists yet (the revision is the next-expected,
                        // not a written record). Resolve the watermark by reading the stream's last record so
                        // every server checkpoint carries a log position, uniform with FilterCheckpoint — the
                        // processor then treats all server watermarks the same, by type, with no special-casing.
                        if (position.LogPosition == LogPosition.Unset && position.Revision != Revision.Unset) {
                            // The V1 CaughtUp message carries the stream revision but no stream name
                            // (proto/kurrentdb/protocol/v1/streams.proto CaughtUp), so the mapped position has an
                            // unset stream. This branch only fires for a stream subscription, so recover the name
                            // from the request — same source as the StreamNotFound branch in the connection phase.
                            StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                            // Read the stream's last record for the watermark. Use None: a subscription winding
                            // down (cancellation) must not abort this bounded metadata read and leave the
                            // checkpoint without a log position. If the stream has no records, keep it as-is.
                            var last = await ReadStream(
                                    stream,
                                    new ReadStreamOptions { Start = Revision.Max, Direction = ReadDirection.Backwards, Limit = 1, SkipDecoding = true },
                                    CancellationToken.None)
                                .Where(r => r.IsRecord)
                                .FirstOrDefaultAsync(CancellationToken.None)
                                .ConfigureAwait(false);

                            // Check the concrete value, not IsRecord: a default ReadMessage (empty
                            // read) reports IsRecord == true with a null payload.
                            if (last.Value is Record lastRecord)
                                checkpoint = checkpoint with { Positions = [lastRecord.Position] };
                        }

                        yield return checkpoint;
                        break;
                    }

                    case ReadResp.ContentOneofCase.Event:
                        var ctx = CreateMappingContext(ct);
                        var record = await resp.Event
                            .MapToRecordAsync(sequencer.FetchNext(), ctx, options.SkipDecoding, options.ThrowOnUnresolved)
                            .ConfigureAwait(false);

                        // Same gating as the read path: when the filter is fully server-side
                        // (filtersLocally == false) the server already delivered exactly its matches,
                        // resolved per ResolveLinks — re-running IsMatch would drop records the server
                        // matched. Only a client-evaluated filter re-checks each record here.
                        if (!filtersLocally || options.Filter.IsMatch(record))
                            yield return record;

                        // When the SDK filters locally, raise a scan checkpoint every window —
                        // matched or not, the record was evaluated, so the watermark can move past
                        // it. This carries progress across the records discarded above, which the
                        // consumer never sees and so could never checkpoint on. (When the filter is
                        // fully server-side this is off: deliveries advance progress on their own,
                        // and a filtered $all already gets server checkpoints.)
                        if (filtersLocally && ++scannedSinceCheckpoint >= options.MaxSearchWindow) {
                            scannedSinceCheckpoint = 0;

                            // Carry the full record position (stream + revision), but pin the log
                            // half to the position the scan has physically reached. On $all that is
                            // the ReadEvent-level commit position — link resolution (on by default)
                            // maps the record position to an earlier target event, so a
                            // record-position watermark would regress while the scan advances. On a
                            // stream subscription the ReadEvent carries no log position, so the
                            // record's own position is the scan position.
                            var scanPosition = resp.Event.HasCommitPosition
                                ? LogPosition.From((long)resp.Event.CommitPosition)
                                : record.Position.LogPosition;

                            yield return new FilterCheckpoint(CheckpointId.New(), record.Timestamp) {
                                Positions = [record.Position with { LogPosition = scanPosition }]
                            };
                        }

                    break;
                }
            }
        }
        finally {
            grpcSession.Dispose();
        }
    }
}
