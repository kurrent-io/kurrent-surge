using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Processors.Partitioning;
using Kurrent.Client.Schema;
using Kurrent.Client.Schema.Serialization;
using Kurrent.Client.Streams;
using Kurrent.Client.Streams.Checkpoints;
using Kurrent.Processors.Models;
using Kurrent.Processors.Options;
using Kurrent.Variant;
using KurrentDB.Protocol.V1.Streams;

namespace Kurrent.Processors.Gateway;

sealed class SubscriptionSource : ClientModuleBase, ISubscription {
    readonly StreamsClient                   _streams;
    readonly ProcessorOptions                _options;
    readonly ChannelWriter<SubscribeMessage> _writer;

    internal SubscriptionSource(
        KurrentClient client,
        ProcessorOptions options,
        ChannelWriter<SubscribeMessage> writer
    ) : base(client) {
        _streams = client.Streams;
        _options = options;
        _writer  = writer;
    }

    public async Task Subscribe(
        RecordPosition startPosition,
        IRecordFilter filter,
        bool skipDecoding,
        CancellationToken ct
    ) {
        try {
            var subscription = _streams.Subscribe(
                SerializerProvider, MetadataDecoder,
                filter, startPosition, skipDecoding,
                ct);

            await foreach (var msg in subscription.ConfigureAwait(false)) {
                if (msg.Value is Record record) {
                    var partition = PartitionCalculator.XXHash
                        .Calculate(record.Stream, _options.MaxParallelism);

                    record = record with { Position = record.Position with { Partition = Partition.From(partition) } };

                    await _writer.WriteAsync(record, ct).ConfigureAwait(false);
                }
                else {
                    await _writer.WriteAsync(msg, ct).ConfigureAwait(false);
                }
            }

            _writer.TryComplete();
        }
        catch (OperationCanceledException) {
            _writer.TryComplete();
        }
        catch (ChannelClosedException) {
            _writer.TryComplete();
        }
        catch (RpcException rex) when (rex.StatusCode == StatusCode.Cancelled) {
            _writer.TryComplete();
        }
        catch (Exception ex) {
            _writer.TryComplete(ex);
        }
    }

    public ValueTask DisposeAsync() =>
        ValueTask.CompletedTask;
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
        SessionId                          sessionId;

        // ── Connection phase ── throws directly to caller, no yield here
        try {
            grpcSession = streams.LegacyServiceClient.Read(request, cancellationToken: ct);

            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();

                var info = await streams.GetStreamInfo(stream, ct).ConfigureAwait(false);

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

        try {
            while (await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false)) {
                var resp = grpcSession.ResponseStream.Current;

                switch (resp?.ContentCase) {
                    case ReadResp.ContentOneofCase.Checkpoint: 
                        yield return resp.Checkpoint.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.CaughtUp: 
                        yield return resp.CaughtUp.MapToCheckpoint(sequencer.FetchNext()); break;

                    case ReadResp.ContentOneofCase.Event:
                        var record = await ProtocolMapping
                            .MapToRecordAsync(
                                resp.Event,
                                sequencer.FetchNext(),
                                serializerProvider, metadataDecoder,
                                SchemaRegistryPolicy.NoRequirements,
                                skipDecoding,
                                ct)
                            .ConfigureAwait(false);

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