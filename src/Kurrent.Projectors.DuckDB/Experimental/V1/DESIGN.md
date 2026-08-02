# Projectors V1 — how it works

> Superseded design note: this file used to specify a `ProjectionDispatcher<TClient>`, a non-generic
> `IProjection` marker, a `ProjectionRuntime<TClient>` bridge, a `Drone` switch arm, and a
> `ProcessorOptions.CheckpointStore` slot. **None of that exists.** All of it was removed once the real
> problem was named — see the feature design doc for why:
> `.claude/context/docs/features/2026-07-27-1200-transactional-projectors/design/design.md`

## The problem

A projection is a sink that is also the **offset authority**. Its resume position lives in its own
database, written in the same transaction as the projected rows — that is what buys exactly-once
without two-phase commit. So at startup the sink has to tell the framework where the source should
start. Control flows backwards.

The drone resolves its start position as its very first act, before any processor callback has run. So
the only thing needed was a lifecycle phase in front of that.

## Core surface — two members

```csharp
public interface IProcessor {
    ValueTask OnInitialize(InitializeContext context) => ValueTask.CompletedTask;
    // OnConnected / OnCheckpoint unchanged
}

public record InitializeContext : DroneContext {
    public required Partition[]                 Partitions      { get; init; }
    public required SubscriptionInitialPosition InitialPosition { get; init; }
    public required LogPosition                 StartPosition   { get; init; }

    public void OwnCheckpoints(RecordPosition[] resumeFrom);
}
```

`OwnCheckpoints` transfers **load, commit and reset together**. Taking half of it diverges the two
records of progress and the projection either replays forever or skips silently. The drone stops
writing its own checkpoint store for that processor.

## This package

```
Abstractions/
  IProjectionTransaction<out TClient>      Begin / Commit / Rollback
  IProjectionCheckpointStore<in TClient>   EnsureStoreExists / Load / Store / Reset — stateless,
                                           client passed per call
  ProjectionBase<TClient>                  IRecordBatchProcessor + IAsyncDisposable. Composes the two
  Projection / DocumentProjection / UpsertProjection
DuckDB/
  DuckDBProjectionTransaction              connection held for the run, transaction per epoch
  DuckDBProjectionCheckpointStore          $projection_checkpoints, PK (processor_id, partition)
  AppendProjection                         appender shape — see the hazard below
  DuckDBProjector                          the only place a projection is wired
Quack/
  QuackProjectionTransaction               TransactionScope value-type handling
  QuackProjector                           reuses the DuckDB store via `in TClient` contravariance
```

## Lifecycle

```
OnInitialize   Begin → EnsureStoreExists → Setup → [OnReset + Reset if StartPosition set]
               → Load → COMMIT → OwnCheckpoints(positions)
ProcessBatch   Begin (idempotent) → Apply into the open transaction. No commit.
               Framework auto-tracks the batch frontier.
OnCheckpoint   Store(expected, ctx.Positions) → COMMIT → Begin next epoch
failure        ProcessBatch catch → Rollback + latch aborted → rethrow
               shutdown barrier → OnCheckpoint throws → drone skips FlushUpTo → positions never move
restart        OnInitialize reads the last committed row → the epoch replays
```

`OnInitialize` commits its own transaction: schema and any rewind are startup facts that must survive
an epoch that later rolls back.

## The guard

```sql
UPDATE ... SET log_position = @next
 WHERE processor_id = @id AND partition = @p AND log_position = @expectedFloor
```

Zero rows means something else moved the row — throw, and the transaction takes the projected rows with
it. Monotonicity and concurrency in one statement. A partition with no row yet falls through to an
`INSERT ... ON CONFLICT DO NOTHING`, whose own conflict proves the second case.

## Hazards

**Appender lifetime.** Verified against `DuckDB.NET.Data.Full` 1.5.3: an appender joins the enclosing
transaction only if it is closed or disposed **before** that transaction commits or rolls back. Left
open across a rollback, the rollback reports success and the count reads 0 — then disposing the appender
flushes the rows in anyway, silently. `AppendProjection` is safe only because its appender is
`using`-scoped inside `Apply`. Hoisting it to a field breaks exactly-once with no visible symptom.

**Never call `ctx.Track` from a checkpoint owner's `OnCheckpoint`.** It would commit one set of
positions and leave the drone flushing a larger one.

**Reset is positions only by default.** `ProjectionBase.OnReset(client, position)` is where a projection
discards state that a replay would duplicate. The default no-op is correct only for idempotent writes.

**Ancestor-namespace binding.** `Kurrent.Projectors` is an ancestor of this package's namespace and
ships unrelated types with the same names (`Projection<T>`, `DuckDBProjection`). Ancestor lookup beats a
`using` import, so base classes here are spelled fully qualified. The symptom of getting it wrong is a
confusing delegate-arity error, not a missing type.

## Not covered

Multi-partition. `ProcessorOptions.MaxParallelism` is `internal` to `Kurrent.Surge.Core`, so no
consumer-facing test can set it — the per-partition checkpoint rows are exercised at one partition only.
