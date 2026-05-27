using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Grpc.Core;
using Kurrent.Client.Exceptions;
using Kurrent.Client.Legacy;
using Kurrent.Client.Streams.Checkpoints;
using KurrentDB.Protocol.V1.Streams;
using Microsoft.Extensions.Logging;
using Polly;

namespace Kurrent.Client.Streams;

delegate ValueTask<AsyncServerStreamingCall<ReadResp>> SubscriptionConnect(RecordPosition start, CancellationToken ct);

sealed class SubscribeState(
    ChannelWriter<SubscribeMessage> writer,
    RecordFilter filter,
    RecordPosition cursor,
    RecordMapper recordMapper,
    SubscriptionConnect connect
) {
    public ChannelWriter<SubscribeMessage> Writer       { get; } = writer;
    public RecordFilter                    Filter       { get; } = filter;
    public RecordPosition                  Cursor       { get; set; } = cursor;
    public RecordMapper                    RecordMapper { get; } = recordMapper;
    public SubscriptionConnect             Connect      { get; } = connect;
    public SequenceGenerator               Sequencer    { get; } = new();
}

public partial class StreamsClient {
    /// <summary>
    /// Subscribes with automatic reconnection on transient errors.
    /// Uses a bounded channel to decouple the gRPC stream producer from the consumer,
    /// allowing Polly to retry the streaming phase while the caller reads from the
    /// channel as a continuous <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On each reconnect, the subscription resumes from the last observed position
    /// (checkpoint or record), so the consumer sees a seamless stream with no gaps.
    /// </para>
    /// <para>
    /// Connection errors are never retried. Only transient streaming errors
    /// (<see cref="RpcException"/>) trigger Polly retries. All exceptions are
    /// translated to domain types before reaching the consumer.
    /// </para>
    /// <para>
    /// Uses <see cref="ResiliencePipeline.ExecuteOutcomeAsync{TResult,TState}"/> so
    /// non-retryable errors (connection failures, terminal errors) flow through
    /// as <see cref="Outcome{TResult}"/> without throwing, avoiding exception overhead
    /// on non-retryable paths.
    /// </para>
    /// </remarks>
    public async IAsyncEnumerable<SubscribeMessage> SubscribeWithResilience(
        RecordFilter filter,
        RecordPosition start,
        bool skipDecoding,
        SubscribeResilienceOptions? resilience = null,
        bool resolveLinks = true,
        uint maxSearchWindow = StreamsClientV1Mapper.DefaultMaxSearchWindow,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        var options = resilience ?? SubscribeResilienceOptions.Default;

        var channel = Channel.CreateBounded<SubscribeMessage>(
            new BoundedChannelOptions(options.ChannelCapacity) {
                SingleWriter = true,
                SingleReader = true,
                FullMode     = BoundedChannelFullMode.Wait
            });

        var pipeline = BuildResiliencePipeline(options);

        _ = Task.Run(Produce, ct);

        await foreach (var msg in channel.Reader.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            yield return msg;

        async Task Produce() {
            var writer  = channel.Writer;
            var mapper  = new RecordMapper(SerializerProvider, MetadataDecoder, skipDecoding);
            var state   = new SubscribeState(writer, filter, start, mapper, (pos, token) => Connect(filter, pos, resolveLinks, maxSearchWindow, token));
            
            var context = ResilienceContextPool.Shared.Get(false, ct);

            try {
                var outcome = await pipeline.ExecuteOutcomeAsync(
                    static async (ctx, s) => {
                        AsyncServerStreamingCall<ReadResp>? session = null;

                        try {
                            session = await s.Connect(s.Cursor, ctx.CancellationToken)
                                .ConfigureAwait(false);

                            await StreamMessages(s, session, ctx.CancellationToken)
                                .ConfigureAwait(false);

                            return Outcome.FromResult(true);
                        } catch (Exception ex) {
                            return Outcome.FromException<bool>(ex);
                        } finally {
                            session?.Dispose();
                        }
                    }, context, state)
                    .ConfigureAwait(false);

                if (outcome.Exception is null or OperationCanceledException)
                    writer.TryComplete();
                else
                    writer.TryComplete(outcome.Exception.AsWellKnownException());
            } finally {
                ResilienceContextPool.Shared.Return(context);
            }
        }
    }

    static async ValueTask StreamMessages(
        SubscribeState state,
        AsyncServerStreamingCall<ReadResp> session,
        CancellationToken ct
    ) {
        while (await session.ResponseStream.MoveNextSafe(ct).ConfigureAwait(false)) {
            var resp = session.ResponseStream.Current;

            switch (resp.ContentCase) {
                case ReadResp.ContentOneofCase.Checkpoint:
                    var checkpoint = resp.Checkpoint.MapToCheckpoint(state.Sequencer.FetchNext());
                    await state.Writer.WriteAsync(checkpoint, ct).ConfigureAwait(false);
                    state.Cursor = checkpoint.Positions[0];
                    break;

                case ReadResp.ContentOneofCase.CaughtUp:
                    var caughtUp = resp.CaughtUp.MapToCheckpoint(state.Sequencer.FetchNext());
                    await state.Writer.WriteAsync(caughtUp, ct).ConfigureAwait(false);
                    state.Cursor = caughtUp.Positions[0];
                    break;

                case ReadResp.ContentOneofCase.Event:
                    var record = await state.RecordMapper
                        .MapAsync(resp.Event, state.Sequencer.FetchNext(), ct)
                        .ConfigureAwait(false);

                    if (state.Filter.IsMatch(record))
                        await state.Writer.WriteAsync(record, ct).ConfigureAwait(false);

                    state.Cursor = record.Position;
                    break;
            }
        }
    }

    /// <summary>
    /// Establishes a gRPC subscription stream. Terminal errors (stream deleted,
    /// tombstoned, position not found) are translated to domain exceptions.
    /// Transient errors propagate as raw <see cref="RpcException"/> so Polly
    /// can inspect the status code directly.
    /// </summary>
    async ValueTask<AsyncServerStreamingCall<ReadResp>> Connect(
        RecordFilter filter,
        RecordPosition start,
        bool resolveLinks,
        uint maxSearchWindow,
        CancellationToken ct
    ) {
        var request = StreamsClientV1Mapper.Requests.CreateSubscriptionRequest(
            filter, start, resolveLinks, maxSearchWindow);

        var grpcSession = LegacyServiceClient.Read(request, cancellationToken: ct);

        try {
            await grpcSession.ResponseStream.MoveNext(ct).ConfigureAwait(false);

            if (grpcSession.ResponseStream.Current?.ContentCase is ReadResp.ContentOneofCase.StreamNotFound) {
                StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                var        info   = await GetStreamInfo(stream, ct).ConfigureAwait(false);

                throw info.State switch {
                    StreamState.Missing    => new StreamNotFoundException(stream),
                    StreamState.Deleted    => new StreamDeletedException(stream),
                    StreamState.Tombstoned => new StreamTombstonedException(stream),
                    StreamState.Active     => new StreamNotFoundException(stream)
                };
            }

            if (grpcSession.ResponseStream.Current?.ContentCase is not ReadResp.ContentOneofCase.Confirmation)
                throw new UnreachableException(
                    $"First message must be the subscription confirmation but got: " +
                    $"{grpcSession.ResponseStream.Current?.ContentCase}");

            return grpcSession;
        } catch (Exception ex) {
            grpcSession.Dispose();

            if (ex is RpcException rex) {
                if (rex.StatusCode == StatusCode.Cancelled)
                    throw new OperationCanceledException(rex.Message, rex);

                if (rex.IsLegacyError(LegacyErrorCodes.StreamDeleted)) {
                    StreamName stream = request.Options.Stream.StreamIdentifier.StreamName.ToStringUtf8();
                    throw new StreamTombstonedException(stream);
                }

                if (rex.IsLegacyError(LegacyErrorCodes.LogPositionNotFound)) {
                    var logPosition = request.Options.All.Position?.CommitPosition ?? LogPosition.Unset;
                    throw new LogPositionNotFoundException(logPosition, rex);
                }
            }

            throw;
        }
    }

    ResiliencePipeline BuildResiliencePipeline(SubscribeResilienceOptions options) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(rex => options.RetryableStatusCodes.Contains(rex.StatusCode))
                    .Handle<IOException>(),
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay            = options.BaseDelay,
                MaxDelay         = options.MaxDelay,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry = args => {
                    Logger.LogWarning(
                        args.Outcome.Exception,
                        "Subscription reconnecting in {Delay} (attempt {Attempt})",
                        args.RetryDelay, args.AttemptNumber + 1);

                    return default;
                }
            })
            .Build();
}
