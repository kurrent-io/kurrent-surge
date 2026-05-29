using Grpc.Core;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Kurrent.Streaming.Gateway;

/// <summary>
/// Shared resilience for every subscription path. Wraps <see cref="StreamsClient.Subscribe"/>
/// in a Polly retry pipeline that reconnects on transient gRPC errors, resuming from the last
/// observed position so the consumer sees a seamless stream with no gaps.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="StreamsClient.SubscribeWithResilience"/> (which decouples producer from
/// consumer through a bounded channel to expose an <see cref="IAsyncEnumerable{T}"/>), the
/// streaming sources already own their output channel. So this primitive drives the retry loop
/// directly and hands each message to a caller-supplied <paramref name="onMessage"/> callback —
/// no extra buffering hop, no background task.
/// </para>
/// <para>
/// <see cref="StreamsClient.Subscribe"/> surfaces transient failures as raw
/// <see cref="RpcException"/> on both the connect path (its <c>AsWellKnownException</c>
/// translation returns the original exception when no domain parser matches a generic status
/// code) and the streaming path (<c>MoveNextSafe</c> only swallows cancellation). Terminal
/// domain errors (stream deleted, position not found) are already translated and never match the
/// retry predicate, so they propagate as-is.
/// </para>
/// </remarks>
static class ResilientSubscription {
    /// <summary>
    /// Builds the reconnection pipeline. Build once per subscription (it is immutable and
    /// reusable across reconnects) — not per message.
    /// </summary>
    public static ResiliencePipeline BuildPipeline(ResilienceOptions options, ILogger logger) =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(rex => options.RetryableStatusCodes.Contains(rex.StatusCode))
                    .Handle<IOException>(),
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay            = options.BaseDelay,
                MaxDelay         = options.MaxDelay,
                BackoffType      = DelayBackoffType.Exponential,
                UseJitter        = true,
                OnRetry = args => {
                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Subscription reconnecting in {Delay} (attempt {Attempt})",
                        args.RetryDelay, args.AttemptNumber + 1);

                    return default;
                }
            })
            .Build();

    /// <summary>
    /// Runs the resilient subscription loop, invoking <paramref name="onMessage"/> for each
    /// message until the stream ends, the token is cancelled, or a terminal error occurs.
    /// Terminal errors (and retry exhaustion) are rethrown so the caller can complete its
    /// channel; retryable errors never surface — Polly reconnects from the advanced cursor.
    /// </summary>
    public static ValueTask PumpAsync(
        StreamsClient streams,
        ResiliencePipeline pipeline,
        RecordFilter filter,
        RecordPosition start,
        bool skipDecoding,
        Func<SubscribeMessage, CancellationToken, ValueTask> onMessage,
        CancellationToken ct
    ) {
        // One allocation per subscription. The cursor lives here so it survives across retries:
        // Polly re-invokes the callback, which reads the advanced cursor and reconnects from it.
        var state = new PumpState(streams, filter, skipDecoding, onMessage) { Cursor = start };

        return pipeline.ExecuteAsync(
            static async (s, token) => {
                await foreach (var msg in s.Streams
                    .Subscribe(s.Filter, s.Cursor, s.SkipDecoding, ct: token)
                    .ConfigureAwait(false)) {
                    // Hand off first, then advance: if a later MoveNext faults, we reconnect
                    // past the message we already delivered — resume is exclusive, so no
                    // duplicate and no gap.
                    await s.OnMessage(msg, token).ConfigureAwait(false);
                    s.Cursor = Advance(s.Cursor, msg);
                }
            },
            state, ct);
    }

    /// <summary>
    /// Advances the resume cursor from an observed message. Records carry a full position;
    /// server checkpoints (filtered <c>$all</c> <see cref="IntervalCheckpoint"/>s and
    /// <see cref="CaughtUpCheckpoint"/>s) carry the scan position, letting us skip empty regions
    /// on reconnect instead of re-scanning the whole range. A <see cref="CaughtUpCheckpoint"/>
    /// can arrive before any position is known, so guard against an unset position — resuming
    /// from <see cref="LogPosition.Unset"/> trips the wire mapper's assertion.
    /// </summary>
    static RecordPosition Advance(RecordPosition cursor, SubscribeMessage msg) =>
        msg.Value switch {
            Record record => record.Position,
            Checkpoint { Positions.Count: > 0 } cp when cp.Positions[^1].LogPosition != LogPosition.Unset
                => cp.Positions[^1],
            _ => cursor
        };

    // Carries the loop state into the static callback so no closure is allocated per retry.
    sealed class PumpState(
        StreamsClient streams,
        RecordFilter filter,
        bool skipDecoding,
        Func<SubscribeMessage, CancellationToken, ValueTask> onMessage
    ) {
        public StreamsClient                                         Streams      { get; } = streams;
        public RecordFilter                                          Filter       { get; } = filter;
        public bool                                                  SkipDecoding { get; } = skipDecoding;
        public Func<SubscribeMessage, CancellationToken, ValueTask>  OnMessage    { get; } = onMessage;
        public RecordPosition                                        Cursor       { get; set; }
    }
}
