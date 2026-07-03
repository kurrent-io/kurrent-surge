using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Processors.Checkpointing;

namespace Kurrent.Streaming.Pipeline.Pragmatic;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// VARIANT 1 — PRAGMATIC. Reuse the Drone's real types. The source yields the existing
// BatchedSubscribeMessage union (a record batch OR a server checkpoint), and the engine reuses the
// existing Checkpointer for per-partition tracking + commit. The engine KNOWS KurrentDB's message
// shape — that is the price of maximal reuse, and it is the Drone refactored with sink fan-out.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A source that yields a stream of <typeparamref name="S"/> and resumes from per-partition floors.</summary>
public interface ISource<S> {
    /// <summary>Resume reading from the loaded per-partition floors (subscribe from their min, prune the rest).</summary>
    ValueTask Restore(RecordPosition[] startPositions, CancellationToken ct = default);

    /// <summary>The message stream: record batches interleaved with in-band server checkpoints.</summary>
    IAsyncEnumerable<S> Read(CancellationToken ct = default);
}

/// <summary>Transforms a record into zero or more outputs, emitted straight to the sinks.</summary>
public interface IProcessor {
    ValueTask Process(ProcessContext context, CancellationToken ct = default);
}

/// <summary>Per-record context. <see cref="Emit"/> goes straight to every sink — ungated.</summary>
public sealed class ProcessContext(Record record, Func<object, CancellationToken, ValueTask> emit) {
    public Record     Record => record;
    public ValueTask  Emit(object output, CancellationToken ct = default) => emit(output, ct);
}

/// <summary>
/// The KurrentDB source. Yields the existing batched union; the batched stream is produced by the
/// existing machinery — the data source feed → channel →
/// <c>ReadBatched&lt;SubscribeMessage, Record, BatchedSubscribeMessage&gt;(...)</c> — i.e. exactly
/// <c>Drone.Run</c>'s stream, lifted out unchanged.
/// </summary>
public sealed class KurrentDbDataSource(IAsyncEnumerable<BatchedSubscribeMessage> batched) : ISource<BatchedSubscribeMessage> {
    public ValueTask Restore(RecordPosition[] startPositions, CancellationToken ct = default) =>
        // Subscription starts at min(startPositions); the caught-up prefix is pruned per partition
        // (Drone.RunAsync + PruneCaughtUp). Wired by whoever builds `batched`.
        ValueTask.CompletedTask;

    public IAsyncEnumerable<BatchedSubscribeMessage> Read(CancellationToken ct = default) => batched;
}

/// <summary>
/// VARIANT 1 engine. Reuses the existing <see cref="Checkpointer"/>. Switches on the KurrentDB message
/// union: record batches are processed + fanned to the sinks; an in-band checkpoint runs the barrier.
/// The cursor is the Checkpointer's per-partition frontier.
/// </summary>
// internal: it reuses the internal Checkpointer, so it cannot be public (CS0051).
sealed class PipelineEngine(
    ISource<BatchedSubscribeMessage> source,
    IProcessor                       processor,
    IReadOnlyList<ISink>             sinks,
    Checkpointer                     checkpointer) {

    public async Task Run(RecordPosition[] startPositions, CancellationToken ct = default) {
        // RESTORE — the host already called Checkpointer.Initialize and got these floors; hand them over.
        await source.Restore(startPositions, ct).ConfigureAwait(false);

        await foreach (var msg in source.Read(ct).WithCancellation(ct).ConfigureAwait(false)) {
            switch (msg.Value) {
                case RecordBatch batch:
                    foreach (var record in batch) {
                        await processor.Process(new ProcessContext(record, Dispatch), ct).ConfigureAwait(false);
                        checkpointer.Track(record.Position);   // track AFTER emit — the frontier is emit progress
                    }
                    break;

                case Checkpoint checkpoint:                    // in-band barrier (server watermark / interval / manual / shutdown)
                    await Barrier(checkpoint, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    // Emit seam: straight to every sink, ungated.
    async ValueTask Dispatch(object output, CancellationToken ct) {
        foreach (var sink in sinks)
            await sink.Write(output, ct).ConfigureAwait(false);
    }

    // THE BARRIER: when a checkpoint flows through the stream, make every sink durable, THEN commit the
    // tracked frontier. It is Drone.HandleCheckpoint + the sink flush. Nothing fires it but the stream.
    async ValueTask Barrier(Checkpoint checkpoint, CancellationToken ct) {
        var context = new SinkCheckpointContext {
            CheckpointId = checkpoint.Id,
            Trigger      = checkpoint.TriggerKind,
            Timestamp    = checkpoint.Timestamp
        };

        foreach (var sink in sinks)                                  // ① flush: make emitted-since-last-barrier durable
            await sink.OnCheckpoint(context, ct).ConfigureAwait(false);

        var positions = checkpointer.PendingPositions();             // ② the cursor = per-partition tracked frontier
        if (positions.Length == 0)
            return;

        await checkpointer.CommitCheckpoint(checkpoint.Id.ToString(), positions, ct).ConfigureAwait(false);

        foreach (var sink in sinks)                                  // ③ commit (no-op for at-least-once)
            await sink.OnCheckpointCommit(checkpoint.Id, ct).ConfigureAwait(false);
    }
}
