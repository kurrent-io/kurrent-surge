using System.Runtime.CompilerServices;
using Kurrent.Client.Processors.Gateway.Messages;
using Kurrent.Client.Streams;
using Kurrent.Streaming.Models;

namespace Kurrent.Streaming.Pipeline.Agnostic;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// VARIANT 2 — AGNOSTIC, NON-GENERIC. The engine deals in `object` + abstract signals, exactly like
// your non-generic ISink. No <S> virus — types live ONLY at the user edges (DataProcessor<T>, DataSink<T>).
// The barrier IS the existing Checkpoint (no positions). At a barrier the engine ASKS the source for its
// committable state; a real, typed checkpoint store persists it. No cursor rides the barrier, no raw bytes.
// ─────────────────────────────────────────────────────────────────────────────────────────────

/// <summary>A signal from a source: a batch of messages, or a barrier.</summary>
public abstract record SourceSignal;

/// <summary>A batch of messages to process. Carried as <c>object</c> — the engine never names the source's type.</summary>
public sealed record SourceData(IReadOnlyList<object> Messages) : SourceSignal;

/// <summary>
/// A checkpoint barrier. It IS the existing <see cref="Checkpoint"/> — id, trigger, timestamp — i.e. a
/// checkpoint WITHOUT positions. The positions/state are not on the barrier; the engine asks the source
/// for them at this point via <see cref="ISource.Snapshot"/>.
/// </summary>
public sealed record CheckpointBarrier(CheckpointId Id, CheckpointTrigger Trigger, DateTimeOffset Timestamp) : SourceSignal;

/// <summary>
/// Source-agnostic, non-generic. Restore from its own committable state, read signals, and hand the engine
/// its committable state when asked at a barrier.
/// </summary>
public interface ISource {
    /// <summary>Rehydrate from the last persisted committable state (<c>null</c> on first run).</summary>
    ValueTask Restore(CheckpointState state, CancellationToken ct = default);

    /// <summary>The signal stream: data batches interleaved with in-band barriers.</summary>
    IAsyncEnumerable<SourceSignal> Read(CancellationToken ct = default);

    /// <summary>
    /// The committable state AT the current barrier — everything yielded (and therefore emitted) up to it.
    /// Safe because barriers are in-band: by the time the engine asks, all prior messages are emitted.
    /// </summary>
    CheckpointState Snapshot();
}

/// <summary>Transforms a message into zero or more outputs, emitted straight to the sinks. Non-generic.</summary>
public interface IProcessor {
    ValueTask Process(ProcessContext context, CancellationToken ct = default);
}

/// <summary>Per-message context. <see cref="Emit"/> goes straight to every sink — ungated.</summary>
public sealed class ProcessContext(object message, Func<object, CancellationToken, ValueTask> emit) {
    public object    Message => message;
    public ValueTask Emit(object output, CancellationToken ct = default) => emit(output, ct);
}

/// <summary>
/// Typed edge adapter — mirrors <c>DataSink&lt;T&gt;</c>. Users write strongly-typed processors; the engine
/// stays <c>object</c>. A message that isn't <typeparamref name="TIn"/> is skipped (it's for another processor).
/// </summary>
public abstract class DataProcessor<TIn> : IProcessor {
    protected abstract ValueTask Process(TIn message, ProcessContext context, CancellationToken ct);

    ValueTask IProcessor.Process(ProcessContext context, CancellationToken ct) =>
        context.Message is TIn typed ? Process(typed, context, ct) : ValueTask.CompletedTask;
}

// the default implementation will be exactly for KurrentDB lol
public interface ICheckpointStateStore {
    /// <summary>Loads the latest checkpoint, resolving positions for the given partitions.</summary>
    ValueTask<CheckpointState> LoadAsync(
        string droneId,
        Partition[] partitions,
        CancellationToken ct = default);

    /// <summary>Persists checkpoint positions for the specified partitions.</summary>
    ValueTask StoreAsync(
        string droneId,
        string checkpointId,
        CheckpointState.PartitionState[] partitions,
        CancellationToken ct = default);
}

public sealed record CheckpointState {
    public PartitionState[] Partitions { get; init; } = [];
    
    public record PartitionState(int Partition, byte[] SourceState);
}

/// <summary>
/// VARIANT 2 engine. NO generic parameter — it deals in <c>object</c> + abstract signals and never names a
/// KurrentDB type. Data → process + fan-out to sinks; Barrier → flush sinks, ask the source for its state,
/// persist it, commit.
/// </summary>
public sealed class AgnosticPipeline(
    string processorId, // the unique id of the processor like `super-prx`
    string droneId, // the instance of the worker drone `super-prx-01`
    ISource source,
    IProcessor processor,
    IReadOnlyList<ISink> sinks,
    ICheckpointStateStore store
) {
    public async Task Run(CancellationToken ct = default) {
        var checkpointState = await store.LoadAsync(droneId, [], ct).ConfigureAwait(false);   // RESTORE — typed state, not bytes
        
        await source.Restore(checkpointState, ct).ConfigureAwait(false);

        await foreach (var signal in source.Read(ct).ConfigureAwait(false)) {
            switch (signal) {
                case SourceData data:
                    // i think we need to consider using both methods. batch and single item.
                    foreach (var message in data.Messages)
                        await processor.Process(new ProcessContext(message, Dispatch), ct).ConfigureAwait(false);
                    break;

                case CheckpointBarrier barrier:
                    await HandleBarrier(barrier, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    // Emit seam: straight to every sink, ungated.
    async ValueTask Dispatch(object output, CancellationToken ct) {
        foreach (var sink in sinks)
            await sink.Write(output, ct).ConfigureAwait(false);
    }

    // THE BARRIER: flush sinks → ASK THE SOURCE for its committable state → persist it → commit sinks.
    // No cursor on the barrier: the source owns "where am I", the engine owns "when" (now, after the flush).
    async ValueTask HandleBarrier(CheckpointBarrier barrier, CancellationToken ct) {
        var context = new SinkCheckpointContext {
            CheckpointId = barrier.Id,
            Trigger      = barrier.Trigger,
            Timestamp    = barrier.Timestamp
        };
        
        foreach (var sink in sinks)
            await sink.OnCheckpoint(context, ct).ConfigureAwait(false);                  // ① flush

        var sourceState = source.Snapshot();
        
        await store
            .StoreAsync(droneId, barrier.Id, sourceState.Partitions, ct)
            .ConfigureAwait(false);      // ② ask source, persist its state

        foreach (var sink in sinks) {
            await sink
                .OnCheckpointCommit(barrier.Id, ct)
                .ConfigureAwait(false);       // ③ commit (no-op until 2PC)
        }
    }
}

// ─── KurrentDB Data Source ────────────────────────────────────────

/// <summary>
/// Maps the existing batched union to generic signals. The batched stream is produced by the existing
/// machinery (the data source feed → channel → <c>ReadBatched</c>) — i.e. <c>Drone.Run</c>'s
/// stream, lifted out. This is the only file that names <c>Record</c>, <c>RecordBatch</c>, or <c>Checkpoint</c>.
///
/// SS: this is more complicated because it would hide all that stuff like the Subscription source contrary to the coments above ffs...
/// </summary>
public sealed class KdbDataSource(IAsyncEnumerable<BatchedSubscribeMessage> batched) : ISource {
    // The yield frontier: last position handed downstream, per partition. Mutated only while iterating Read,
    // read only by Snapshot at a barrier — and the iterator is suspended at the barrier, so no race.
    readonly Dictionary<Partition, RecordPosition> _frontier = new();

    public ValueTask Restore(CheckpointState checkpoint, CancellationToken ct = default) {
        var positions = Decode(checkpoint);

        // this _frontier stuff... not sure...dont we need the tracker? whre is the checkpointer in this design?
        // so much to think about
        foreach (var entry in positions)
            _frontier[entry.Partition] = entry;
        
        // Subscribe from min(_frontier) and prune the caught-up prefix per partition (existing Drone logic).
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<SourceSignal> Read([EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var msg in batched.WithCancellation(ct).ConfigureAwait(false)) {
            switch (msg.Value) {
                case RecordBatch batch:
                    // we return the batch.
                    // but i never thought about how do we track positions in this model.
                    // the source owns "where am i", so it must advance its frontier as it yields batches, and then Snapshot just reads it at the barrier.
                    // the frontier is "everything yielded so far", which is safe because the barrier is in-band: by the time we ask for the frontier, all prior messages are emitted.
                    
                    // not sure. need to validate this part.
                    
                    foreach (var record in batch)
                        _frontier[record.Position.Partition] = record.Position;   // log order ⇒ last write per partition is the max

                    yield return new SourceData([..batch]); // Record is a reference type ⇒ fits IReadOnlyList<object>

                break;

                case Checkpoint cp:
                    // here we need to understand that we got positions we must yield the barrier, then ask the source for the state (the frontier) to persist it.
                    // The barrier is the Checkpoint, but the positions are not on it; they are in the source's committable state, which we ask for at this point.
                    yield return new CheckpointBarrier(cp.Id, cp.TriggerKind, cp.Timestamp); // the barrier IS the Checkpoint, positions excluded

                break;
            }
        }
    }

    // The committable state asked for at the barrier: the per-partition frontier yielded so far.
    public CheckpointState Snapshot() => Encode(_frontier.Values.ToArray());

    // For KurrentDB the per-partition source state is just the partition's log position (a long). A future
    // source whose resume token is richer simply puts more bytes in SourceState — the engine never looks inside.
    static CheckpointState Encode(RecordPosition[] positions) =>
        new() {
            Partitions = positions
                .Select(p => new CheckpointState.PartitionState(p.Partition.Value, BitConverter.GetBytes(p.LogPosition.Value)))
                .ToArray()
        };

    static RecordPosition[] Decode(CheckpointState state) =>
        state.Partitions
            .Select(p => RecordPosition.From(
                LogPosition.From(p.SourceState.Length >= sizeof(long) ? BitConverter.ToInt64(p.SourceState) : (long?)null),
                Partition.From(p.Partition)))
            .ToArray();
}
