# Hive — Parallel Drone Spec

**Status:** Prototype. First iteration. Subject to review.
**Date:** 2026-04-17

## Purpose

Run N drones in parallel against one KurrentDB subscription, each drone owning a
disjoint slice of partitions. No leases, no locks, no rebalancing — static assignment
at startup.

The motivation: multiple subscriptions for the same filter force the server to stream
the same records N times and each drone to client-side-filter records it doesn't own.
One shared subscription fanned out by partition avoids that cost.

## Components

### `Hive`
Top-level orchestrator. The thing a user instantiates and starts.

Responsibilities:
- Owns the `KurrentClient` lifecycle (always — pre-built clients are rejected).
- Generates drone IDs, distributes partitions via `PartitionAssigner`, creates the shared `CheckpointStore`.
- Constructs one `MasterSubscriptionSource` and N `Drone`s (each wrapping its own `LocalPartitionedSource`).
- Runs the master pump and every drone concurrently under a single `CancellationToken`.
- Cascading stop: cancel the token → master cancels gRPC → channels complete → drones drain.

Non-responsibilities:
- Does not rebalance. Partition assignment is static.
- Does not decide the distribution strategy — that comes from `HiveOptions.Assigner`.

### `MasterSubscriptionSource`
The one gRPC subscription shared by every drone. Pure transport + checkpoint load.

Responsibilities:
- Opens the real gRPC stream subscription from the global low watermark.
- Loads checkpoints **once** per run (single stream read, in-memory per-drone resolution).
- Drives the periodic checkpoint barrier timer.
- Fan-out: `Connected` → all drones; server-side `Checkpoint` → all drones; timer barrier → all drones.
- Routing: `Record` → the one drone that owns the record's partition.
- On exit (clean or faulted) completes every drone's inbox channel.

Non-responsibilities:
- Does not track per-drone processed positions (each local owns its tracker).
- Does not write checkpoints (each local writes under its own `DroneId`).

### `LocalPartitionedSource`
Per-drone pipe. Implements `ISubscriptionSource` — indistinguishable from the real source
from the drone's perspective.

Responsibilities:
- Owns a bounded inbox `Channel<SubscribeMessage>` fed by the master.
- Registers with the master at construction (partition ownership + writer).
- Owns its `PartitionedPositionTracker` and `_pendingFlushes` dictionary.
- Catch-up filter: drops records older than its own start position for the same partition
  (master opens gRPC at the *global* low watermark, so a drone further ahead will see
  records it already processed).
- On checkpoint barrier: snapshots its tracker, yields a filled `Checkpoint`.
- On `CompleteCheckpointAsync`: writes to the shared `CheckpointStore` tagged with its
  own `DroneId`.

Non-responsibilities:
- Does not open gRPC.
- Does not load checkpoints.
- Does not run its own timer.

### `HiveOptions`
Knobs: `DroneCount`, `Assigner` (defaults to `StickyStrategy`), nested `ProcessorOptions`.

## Lifecycle

```
Hive.RunAsync(ct)
 ├─ validate (reject pre-built client; reject DroneCount > MaxParallelism)
 ├─ resolve processor identity (ProcessorId auto-derived if unset)
 ├─ build client, build CheckpointStore
 ├─ PartitionAssigner.Distribute(Empty, droneIds, MaxParallelism) → manifest
 ├─ create Master
 ├─ for each droneId:
 │    ├─ create LocalPartitionedSource (registers routes with Master)
 │    └─ create Drone (wraps the local)
 ├─ await Master.InitializeAsync(ct)      // single checkpoint load, resolves per-drone slice + low watermark
 └─ Task.WhenAll(Master.RunAsync(ct), drones.RunAsync(ct))
```

## Checkpointing

One checkpoint stream, many writers. `$processors/{processorId}/checkpoints`, all writes
with `ExpectedStreamState.Any`, each write tagged with the drone's `DroneId`.

Barrier flow:
1. Master timer fires every `Checkpoint.Interval`.
2. Master emits an empty `Checkpoint` message and **fans it to every drone's inbox**.
3. Each local intercepts the empty checkpoint at yield time, snapshots its own tracker,
   emits a filled `Checkpoint` to the drone.
4. Drone calls `CompleteCheckpointAsync(id)` → local writes `PartitionsCheckpointed`
   with its `DroneId`.

Load flow (at startup, run once by Master):
1. Master calls `CheckpointStore.LoadLatestAsync(ct)` — reads the whole stream once.
2. Master builds a `partition → position` map from the loaded positions.
3. For each drone, Master resolves the drone's partition slice from that map
   (defaulting to `InitialPosition` for partitions with no checkpoint).
4. Master stores per-drone positions; each local pulls its slice from Master on
   first `Subscribe`.

Low watermark: the minimum `RecordPosition` across all loaded partitions. The master's
gRPC subscription starts here so no drone misses records. Records older than a drone's
own position for its partition are dropped by the local's catch-up filter.

## Partition Assignment

- Static. Runs once at startup via `PartitionAssigner.Default.Distribute(Empty, droneIds, MaxParallelism)`.
- `DroneCount ≤ MaxParallelism` is enforced — a drone with no partitions is rejected at startup.
- Default strategy is `StickyStrategy`. Can be swapped via `HiveOptions.Assigner`.

## Back-pressure

Bounded channels with `BoundedChannelFullMode.Wait`. If a drone stalls:
- Its inbox fills up.
- Master's `WriteAsync` blocks on the full channel.
- Master's main loop stalls → every other drone stops receiving.

This is a **documented v1 trade-off**. In practice a well-behaved drone doesn't stall.
A misbehaving drone will be obvious (metrics, logs). If it becomes an operational issue,
the fix is per-partition lag metrics + targeted back-pressure policy, not a ground-up
redesign.

## Out of Scope (v1)

- Dynamic rebalancing / worker add/remove
- Leases / locks / distributed coordination
- Multi-machine deployment (this is single-process for now)
- Server-side partition-filtered subscriptions (not supported by KurrentDB protocol)

## Open Questions

1. **Server-side `Checkpoint` + timer barrier collision.** If the server emits a
   `Checkpoint` (e.g. `CaughtUp`) right before the timer fires, every drone
   snapshots its tracker twice in quick succession. Possible mitigations:
   coalesce within a minimum interval; skip the timer barrier if a server barrier
   just fanned out; drop the server-barrier fan-out entirely and rely only on the
   timer. Deferred.
2. **`PartitionKeySelector` unused.** Master uses `record.Stream` as the hash key
   — same as the existing `SubscriptionSource`. `ProcessorOptions.Subscription.PartitionKeySelector`
   is configurable but never read. Needs wiring, probably at the master's partition
   calculation site.
3. **Processor instance per drone.** `Subscribe.ResolveProcessor()` is called per
   drone. If the user registered a singleton via `Subscribe.Use(instance)`, every
   drone shares it — dangerous for stateful processors. Options: require a factory
   for Hive, reject singleton registrations, or document the constraint.
4. **Duplicate `HybridLogicalClock` instances.** Master and each Local build their
   own clock. The HLC's monotonic guarantee is per-instance — sharing would require
   locking. Intentional duplication, but worth a comment so it isn't "optimized"
   away later.

## File Layout

```
src/Kurrent.Processors/Hive/
├── Hive.cs                      — orchestrator
├── HiveOptions.cs               — config
├── MasterSubscriptionSource.cs  — shared subscription + fan-out + load + timer
├── LocalPartitionedSource.cs    — per-drone pipe
└── SPEC.md                      — this file
```
