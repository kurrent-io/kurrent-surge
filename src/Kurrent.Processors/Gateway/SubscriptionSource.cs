using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Processors.Models;
using Kurrent.Variant;
using KurrentDB.Protocol.V1.Streams;
using Kurrent.Client.Processors.Gateway.Messages;


namespace Kurrent.Processors.Gateway;

sealed class SubscriptionSource : ClientModuleBase, ISubscriptionSource {
    readonly StreamsClient _streams;

    internal SubscriptionSource(KurrentClient client) : base(client) =>
        _streams = new StreamsClient(client);

    public IAsyncEnumerable<SubscribeMessage> Subscribe(
        IRecordFilter filter,
        RecordPosition from,
        bool skipDecoding,
        CancellationToken ct
    ) => _streams.Subscribe(SerializerProvider, MetadataDecoder, filter, from, skipDecoding, ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public readonly record struct Connected(SessionId SessionId);

public readonly partial record struct SubscribeMessage : IVariant<Connected, Record, Checkpoint> {
    public static readonly SubscribeMessage None;
}

static class InternalClientExtensions {
    /// <summary>
    /// Subscribes to a stream or log via the V1 wire protocol.
    /// Connection phase (first message validation) runs eagerly before any yield,
    /// so errors throw directly to the caller.
    /// </summary>
    public static async IAsyncEnumerable<SubscribeMessage> Subscribe(
        this StreamsClient streams,
        ISchemaSerializerProvider serializerProvider,
        IMetadataDecoder metadataDecoder,
        IRecordFilter filter,
        RecordPosition start,
        bool skipDecoding,
        [EnumeratorCancellation] CancellationToken ct
    ) {
        var request = filter.IsStreamFilter
            ? ProtocolMapping.Requests.CreateStreamSubscriptionRequest(filter, start.Revision)
            : ProtocolMapping.Requests.CreateLogSubscriptionRequest(filter, start.LogPosition);

        AsyncServerStreamingCall<ReadResp> grpcSession;
        SessionId sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = streams.LegacyServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            // v1 protocol bull: StreamNotFound is a message, not an exception (unlike 99.9% of other error cases)
            // And we dont know if the stream is missing or deleted until we check the stream info.
            // Meanwhile the stream state might have changed to deleted or tombstoned.
            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await streams.GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    // lets just throw not found if the stream becames active again
                    // and let the user retry. we should do it ourselves in v2
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"EVERYTHING IS BROKEN! First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}"
                );

            sessionId = SessionId.From(Guid.Parse(grpcSession.ResponseStream.Current!.Confirmation.SubscriptionId));
        }
        catch (RpcException rex) {
            if (rex.StatusCode == StatusCode.Cancelled)
                throw new OperationCanceledException(rex.Message, rex);

            // when stream is tombstoned a stream deleted exception is thrown for some weird reason
            if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                throw new StreamTombstonedException(stream);
            }

            // when a position does not exist (targets a position between records in the same stream)
            if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                var position = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                throw new LogPositionNotFoundException(position, rex);
            }

            throw rex.AsWellKnownException();
        }

        yield return new Connected(sessionId);

        // ── Pump phase ── yields Record | Checkpoint
        SequenceGenerator sequencer = new();

        try {
            while (await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint:
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext());
                        break;

                    case ReadResp.ContentOneofCase.CaughtUp:
                        yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext());
                        break;

                    case ReadResp.ContentOneofCase.Event:
                        var record = await ProtocolMapping.MapToRecordAsync(
                            resp.Event,
                            sequencer.FetchNext(),
                            serializerProvider, metadataDecoder,
                            SchemaRegistryPolicy.NoRequirements,
                            skipDecoding,
                            ct
                        ).ConfigureAwait(false);

                        // client-side filter
                        // will implement server side in v2
                        if (filter.IsMatch(record))
                            yield return record;

                        break;
                }
            }
        }
        finally {
            grpcSession.Dispose();
        }
    }
}
