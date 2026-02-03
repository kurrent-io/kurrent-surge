# Kurrent Processors: Hive Mind Coordination Specification

| | |
|---|---|
| **Status** | Draft |
| **Authors** | Sérgio Silveira, Claude |
| **Created** | 2026-01-31 |
| **Last Updated** | 2026-02-01 |

---

## Changelog

| Version | Date | Changes |
|---------|------|---------|
| 0.1.0 | 2026-01-31 | Initial specification |
| 0.2.0 | 2026-01-31 | Refined stream structure (`$processors/{id}` as aggregate, `/control` for signals), ProcessorOptions structure with CoordinationSettings, management operations, deferred items noted |
| 0.3.0 | 2026-02-01 | Leader-based coordination model (inspired by KCL 3.x). Eliminated lease renewal writes: checkpoint-as-liveness. Two-phase handoff (graceful vs forced). gRPC connection as self-fencing. RFC-style decision records. |
| 0.3.1 | 2026-02-01 | Added component model under Architecture (§3): component tree, KCL 3.x mapping, eliminated/deferred components. |

---

## Table of Contents

1. [Overview](#1-overview)
2. [Decision Records](#2-decision-records)
3. [Architecture](#3-architecture)
4. [Stream Structure](#4-stream-structure)
5. [Aggregate Events](#5-aggregate-events)
6. [Control Signals](#6-control-signals)
7. [Lease Document](#7-lease-document)
8. [ProcessorOptions](#8-processoroptions)
9. [Leader Election](#9-leader-election)
10. [Worker Lifecycle](#10-worker-lifecycle)
11. [Crash Detection & Recovery](#11-crash-detection--recovery)
12. [Rebalancing & Handoff](#12-rebalancing--handoff)
13. [Safety Invariants](#13-safety-invariants)
14. [Management Operations](#14-management-operations)
15. [Configuration](#15-configuration)
16. [Next Steps](#16-next-steps)
17. [Deferred Items](#17-deferred-items)
18. [Future Considerations](#18-future-considerations)

---

## 1. Overview

**Hive Mind** is a leader-based coordination model for Kurrent Processors. One elected worker (the leader) acts as the single brain for partition assignment, failure detection, and topology management. All other workers process records, checkpoint progress, and react to the leader's signals. KurrentDB acts as dumb storage — zero server-side coordination logic.

This design is inspired by AWS KCL 3.x's evolution from fully decentralized to leader-based coordination, adapted for KurrentDB's event-driven capabilities.

### Key Properties

- **Zero server changes** — Ships with current KurrentDB
- **Leader-based assignment** — One brain, globally optimal decisions, no OCC races
- **Event-driven coordination** — TopologyChanged signals via filter injection (push, not poll)
- **Checkpoint-as-liveness** — No separate heartbeat or lease renewal writes
- **gRPC connection as self-fencing** — Transport-layer failure detection for the worker itself
- **Optimistic concurrency** — Stream revision = fencing token for lease writes

### What Workers Do

1. Connect via gRPC streaming subscription (session)
2. Process records and checkpoint progress at regular intervals
3. React to `TopologyChanged` signals from the leader (via filter injection)
4. Acquire and release partitions as directed by the leader
5. Self-fence on connection failure (stop immediately)

### What The Leader Does (One Elected Worker)

1. Run assignment cycle periodically (~20s)
2. Read all leases — derive worker liveness from checkpoint timestamps
3. Detect dead workers (stale timestamps) — reassign their partitions
4. Detect new workers (WorkerJoined events) — trigger immediate rebalance
5. Emit `TopologyChanged` signals to the control stream
6. Also process records as a regular worker


---

## 2. Decision Records

This section captures the key architectural decisions, the alternatives considered, and why each was chosen. Structured as lightweight RFC-style records.

### DR-1: Leader-Based vs Fully Decentralized Coordination

**Status:** Accepted

**Context:** v0.2 specified a fully decentralized model where every worker independently reads all leases, calculates assignments, detects failures, and competes to acquire partitions via OCC. This mirrors KCL 2.x's architecture.

KCL spent years in production with the decentralized model before migrating to leader-based (3.x). The problems that drove the change: O(N×P) read amplification, unpredictable convergence during rebalancing (thundering herd), inability to make globally optimal assignment decisions, and lease-stealing heuristics that could oscillate.

**Decision:** Adopt a leader-based model. One elected worker runs the assignment cycle. Workers don't compete for partitions — the leader decides and signals.

**Alternatives Considered:**

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A. Fully decentralized** (v0.2) | Every worker calculates, races, converges | No single point of failure, simpler election-free model | O(N×P) reads, OCC races on acquisition, unpredictable convergence, no global optimality |
| **B. Leader-based** (chosen) | One elected leader computes assignments, signals workers | O(P) reads (leader only), no OCC races, globally optimal decisions, predictable behavior | Requires leader election, brief gap during leader failover |
| **C. Server-side coordinator** | KurrentDB gateway handles assignment | Instant failure detection (TCP), zero heartbeat overhead | Requires server changes — violates our shipping constraint |

**Why B:** We start where KCL ended up, not where it started. The leader election overhead is minimal (one OCC write). Leader failover introduces a brief gap in health checking, but existing assignments continue working — only failure detection is delayed, not processing.

---

### DR-2: Checkpoint-as-Liveness vs Separate Heartbeat

**Status:** Accepted

**Context:** v0.2 specified per-partition lease renewal writes every 10-30 seconds as the liveness mechanism. With 3 workers and 128 partitions, that's ~384 writes per renewal interval just to prove workers are alive.

Workers already checkpoint at regular intervals. Checkpoint writes have timestamps. The leader can derive liveness from those timestamps.

**Decision:** Eliminate separate heartbeat/renewal writes. Checkpoint writes serve as the liveness signal. The checkpoint timer fires regardless of whether progress was made (idle workers write the same position with a fresh timestamp).

**Alternatives Considered:**

| Option | Description | Writes (3 workers, 128 partitions, 10s interval) | Cons |
|--------|-------------|---------------------------------------------------|------|
| **A. Per-partition lease renewal** (v0.2) | Every partition lease renewed on a timer | 384 writes / 10s | Massive write amplification, scales with partitions |
| **B. Per-worker heartbeat stream** | One heartbeat per worker to a dedicated stream | 3 writes / 10s | Extra stream type, still a write that exists only for liveness |
| **C. Checkpoint-as-liveness** (chosen) | Checkpoint timer fires always, leader reads timestamps | 0 additional writes | Detection latency tied to checkpoint interval |

**Why C:** Zero additional writes. The writes needed for progress already prove liveness. Detection latency is configurable via checkpoint interval — want faster failover? Checkpoint more often. The tradeoff is explicit: write frequency vs detection speed.

**Detection formula:** `MissedCheckpointThreshold × CheckpointInterval`. With threshold=2 and interval=30s, worst-case detection is ~60s. With interval=10s and threshold=3, worst-case is ~30s.

---

### DR-3: gRPC Connection as Self-Fencing

**Status:** Accepted

**Context:** Workers subscribe to KurrentDB via gRPC streaming operations. The connection stays open for the lifetime of the subscription — it is effectively a session. When the connection breaks (crash, network failure, graceful shutdown), the worker's gRPC client receives an error immediately.

**Decision:** Workers treat any gRPC stream error as an immediate self-fencing signal. Stop processing. Don't wait for the next checkpoint or lease check. The transport layer told you — trust it.

**Alternatives Considered:**

| Option | Description | Detection Latency | Cons |
|--------|-------------|-------------------|------|
| **A. Fencing via lease revision** (v0.2) | Worker detects fencing on next lease write (renewal or checkpoint) | Up to renewal interval | Requires periodic renewal writes to detect promptly |
| **B. gRPC connection as self-fence** (chosen) | Stream error = stop immediately | Instant | Only works for self-detection — peers don't know |

**Why B:** Instant self-fencing from the transport layer eliminates the need for frequent renewal writes for the fencing use case. Peer detection is handled separately by the leader (DR-2). Two layers: transport for self, leader for peers.

**Note:** This only handles the "I know I'm dead" case. The "others know I'm dead" case is handled by the leader reading stale checkpoint timestamps (DR-2).

---

### DR-4: Two-Phase Handoff (Graceful vs Forced)

**Status:** Accepted

**Context:** When partitions need to change owners (rebalancing, worker death), there are two scenarios with different constraints:

1. **Worker is alive** (rebalancing): The current owner can checkpoint before releasing, minimizing reprocessed records.
2. **Worker is dead** (crash): Nobody to hand off to. Must reassign immediately.

KCL 3.x uses a `pendingOwner` pattern for graceful handoff, falling back to direct reassignment for crashes.

**Decision:** Two distinct paths:

- **Graceful (worker alive):** Leader emits TopologyChanged signal. Workers who lose partitions checkpoint and release. Workers who gain partitions wait for the release, then acquire.
- **Forced (worker dead):** Leader writes leases directly (owner → null) for the dead worker's partitions, then emits TopologyChanged. Surviving workers acquire immediately.

**Alternatives Considered:**

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A. Leader always writes leases** (KCL 3.x style) | Leader writes ownership changes directly for all cases | Simple, atomic, one writer | On rebalance: worker loses partition before checkpointing — up to a full checkpoint interval of records reprocessed |
| **B. Workers always acquire/release** (v0.2 convergent loop) | Leader signals, workers execute all ownership changes | Workers always checkpoint before releasing | Multi-step convergence, OCC races on acquisition, ordering dependencies |
| **C. Two-phase: forced for death, graceful for rebalance** (chosen) | Leader writes directly only for dead workers; living workers release themselves | Minimal reprocessing on rebalance, immediate recovery on crash | Two code paths |

**Why C:** Option A sacrifices data on every rebalance (uncheckpointed records lost). Option B is slow and racy for crash recovery (nobody to release dead worker's leases). Option C uses the right tool for each scenario.

---

### DR-5: Worker Join via Filter Injection (Push, Not Poll)

**Status:** Accepted

**Context:** In KCL 3.x, when a new worker joins, it reports metrics and then polls via GSI query until the leader's next assignment cycle (~20s). The worker sits idle waiting.

KurrentDB has filter injection: the leader's subscription can include the aggregate stream. When a worker writes WorkerJoined, the leader sees it inline with its data — instantly.

**Decision:** New workers write `WorkerJoined` to the aggregate stream. The leader's filter includes this stream. Leader sees the join event and triggers an immediate out-of-cycle assignment, eliminating the polling delay.

**Alternatives Considered:**

| Option | Description | Join Latency |
|--------|-------------|--------------|
| **A. Wait for next assignment cycle** (KCL 3.x) | Worker joins, waits up to 20s for leader's periodic cycle | Up to 20s |
| **B. Worker self-assigns on join** (v0.2) | Worker reads state, calculates, writes leases + signal | Immediate, but two code paths (join vs steady-state) |
| **C. Leader reacts to join event** (chosen) | Worker writes WorkerJoined, leader sees via filter injection, triggers immediate cycle | Seconds (leader compute + write time) |

**Why C:** Leverages our unique advantage over KCL (push via filter injection vs poll). Single assignment code path (leader always assigns). Fast join without self-assignment complexity.

---

## 3. Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         KurrentDB                                           │
│                       (dumb storage)                                        │
│                                                                             │
│  $all ────────────────────────────────────────────────────────────────────── │
│    [user records...]                                                        │
│                                                                             │
│  $processors/{id} ──────────────────────────────────────────────────────── │
│    [ProcessorCreated] [WorkerJoined] [WorkerLeft] ...                      │
│    (event-sourced aggregate, audit trail)                                   │
│                                                                             │
│  $processors/{id}/control ──────────────────────────────────────────────── │
│    [TopologyChanged] [TopologyChanged] ...                                 │
│    (leader signals, injected into worker streams)                          │
│                                                                             │
│  $processors/{id}/leases/{partition} ─────────────────────────────────────  │
│    { owner, checkpoint, timestamp, ... }  (document-sourced)               │
│                                                                             │
│  $processors/{id}/leader ─────────────────────────────────────────────────  │
│    { workerId, timestamp }  (document-sourced, leader election)            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
       ▲                 ▲                 ▲
       │                 │                 │
       │    read/write   │    read/write   │    read/write
       │                 │                 │
  ┌────┴────┐       ┌────┴────┐       ┌────┴────┐
  │Worker A │       │Worker B │       │Worker C │
  │ (LEADER)│       │         │       │         │
  │         │       │         │       │         │
  │ ┌─────┐ │       │ ┌─────┐ │       │ ┌─────┐ │
  │ │Assign│ │       │ │React│ │       │ │React│ │   Leader assigns
  │ │Detect│ │       │ │Ckpt │ │       │ │Ckpt │ │   Workers react
  │ │Signal│ │       │ │Acq/ │ │       │ │Acq/ │ │
  │ │+Ckpt │ │       │ │Rel  │ │       │ │Rel  │ │
  │ └─────┘ │       │ └─────┘ │       │ └─────┘ │
  └─────────┘       └─────────┘       └─────────┘

  One leader. Workers react. KurrentDB is dumb storage.
```

### Role Differentiation

All workers share a common base (process records, checkpoint, react to signals). The leader runs additional logic:

| Responsibility | Leader | Non-Leader Workers |
|----------------|--------|--------------------|
| Process records | ✅ | ✅ |
| Checkpoint (lease writes) | ✅ | ✅ |
| Self-fence on connection loss | ✅ | ✅ |
| React to TopologyChanged | ✅ | ✅ |
| Assignment cycle (~20s) | ✅ | ❌ |
| Death detection (stale leases) | ✅ | ❌ |
| Leader election participation | ✅ | ✅ |

### Filter Injection

Workers subscribe to user data AND control signals in a single stream:

```sql
-- User filter
(schema_name IN ('OrderCreated', 'OrderShipped') AND stream_id LIKE 'orders-%')

-- Injected (control signals from leader)
OR stream_id = '$processors/my-processor/control'
```

Workers see `TopologyChanged` signals inline with records at deterministic log positions.

The leader's subscription additionally includes the aggregate stream to detect worker joins:

```sql
-- Leader's filter (in addition to user data + control)
OR stream_id = '$processors/my-processor'
```

### Components

> **Note:** This section captures the component model as currently understood. Needs further refinement — interfaces, ownership boundaries, and error propagation are not fully worked out yet.

#### Component Overview

```
ProcessorWorker (top-level orchestrator per worker)
│
├── LeaderElector              ← ALL workers
│   └── Claims/renews/detects stale leadership via OCC
│
├── CheckpointManager          ← ALL workers
│   └── Timer-based checkpoint writes (progress + liveness)
│   └── Detects OCC conflicts (fencing)
│
├── AssignmentManager          ← LEADER ONLY
│   └── Periodic assignment cycle (read leases, detect deaths, compute)
│   └── Emits TopologyChanged to control stream
│   └── Forced path: writes leases for dead workers
│   └── Reacts to WorkerJoined for immediate out-of-cycle assignment
│
├── PartitionCalculator        ← Used by AssignmentManager
│   └── Pure computation: workers + current state → assignment map
│   └── Pluggable strategy (naive, sticky, future: CPU-aware)
│
└── CoordinationStore          ← Used by all components
    └── Persistence port over KurrentDB streams
    └── Read/write leases, leader claims, aggregate events, control signals
    └── Encapsulates stream naming, atomic appends, expected revisions
```

TopologyChanged handling is not a separate component — it's part of the ProcessorWorker's subscription message loop. The worker already processes a mixed stream of records, barriers, and control signals via `.Match()`. TopologyChanged is just another message type that triggers acquire/release logic inline.

#### Mapping to KCL 3.x

| KCL 3.x Component | Ours | Difference |
|--------------------|------|------------|
| **Scheduler** | ProcessorWorker | Same role — top-level orchestrator |
| **LeaderDecider** | LeaderElector | Same concept, KurrentDB OCC instead of DynamoDB conditional write |
| **LeaseAssignmentManager** | AssignmentManager | Same role. Ours also handles forced path (dead worker lease writes) |
| *(buried in LAM)* | PartitionCalculator | KCL hardcodes strategy inside LAM. We extract it as pluggable. |
| **LeaseRenewer** | **Eliminated (DR-2)** | KCL writes counter increments every ~3.3s. We don't — checkpoint IS liveness. |
| **LeaseDiscovery** | **Eliminated (DR-5)** | KCL polls GSI to find assignments. We push via TopologyChanged (filter injection). |
| **WorkerMetricStatsReporter** | **Not needed (v1)** | KCL uses CPU metrics for balancing. We start with partition-count balancing. |
| *(DynamoDB — 3 tables)* | CoordinationStore | One system (KurrentDB streams) instead of three DynamoDB tables |
| *(no equivalent)* | CheckpointManager | KCL separates checkpoint and renewal. We unified them into one timer. |

#### What We Eliminated (and Why)

**LeaseRenewer** — KCL's most active component. Runs every ~3.3s, writes a counter increment per lease. With 100 partitions per worker, that's ~30 writes/s just for liveness. Eliminated because checkpoint writes already carry timestamps (DR-2).

**LeaseDiscovery** — KCL workers poll a DynamoDB GSI every cycle to discover "did the leader assign me something new?" Eliminated because TopologyChanged signals arrive inline with data via filter injection (DR-5). Zero polling latency.

#### What KCL Has That We Don't (Yet)

**WorkerMetricStatsReporter / CPU-aware balancing** — KCL 3.x balances by CPU utilization, not just partition count. Hot partitions get moved to underloaded workers. We start with partition-count balancing. V2 enhancement — requires metrics infrastructure we don't have yet.

## 4. Stream Structure

| Stream | Purpose | Content Type | Retention |
|--------|---------|--------------|-----------|
| `$processors/{id}` | Aggregate (lifecycle events, audit trail) | Event-sourced | Forever |
| `$processors/{id}/control` | Signals (leader → workers, injected into worker stream) | Event-sourced | Forever |
| `$processors/{id}/leases/{partition}` | Per-partition ownership and checkpoint | Document-sourced | `$maxCount` = RetentionCount |
| `$processors/{id}/leader` | Leader election | Document-sourced | `$maxCount` = 1 |

### Why This Structure

- **Aggregate stream**: Single source of truth for processor lifecycle. Event-sourced for full audit trail. Leader subscribes to this to detect `WorkerJoined` events instantly via filter injection.
- **Control stream**: Separate from aggregate for filter injection. Workers see `TopologyChanged` signals inline with their data records — push, not poll.
- **Per-partition leases**: Independent writes (no false conflicts between workers). Stream revision = optimistic concurrency version. Lease writes happen only on ownership changes and checkpoints — no renewal writes.
- **Leader stream**: Single-document stream for leader election via OCC. `$maxCount = 1` — only the latest claim matters.

## 5. Aggregate Events

Written to `$processors/{id}`. Event-sourced (full history = audit trail).

### ProcessorCreated

```
kurrent.processors.ProcessorCreated {
  processorId: string
  maxParallelism: int
  subscription: {
    filterExpression: string
    partitionKeySelector: string
    initialPosition: string
  }
  checkpoint: {
    interval: long
    retentionCount: int
  }
  coordination: {
    leaseTimeout: long
    renewalInterval: long
    healthCheckInterval: long
  }
  timestamp: Instant
}
```

### ProcessorDeleted

```
kurrent.processors.ProcessorDeleted {
  processorId: string
  reason: string?
  timestamp: Instant
}
```

### WorkerJoined

```
kurrent.processors.WorkerJoined {
  workerId: string
  timestamp: Instant
}
```

### WorkerLeft

```
kurrent.processors.WorkerLeft {
  workerId: string
  reason: LeaveReason    // Graceful, Expired, Fenced
  timestamp: Instant
}
```

**LeaveReason enum:**
- `Graceful` - Worker shut down normally
- `Expired` - Worker's lease expired (detected dead by peers)
- `Fenced` - Worker was fenced out (zombie detection)

---

## 6. Control Signals

Written to `$processors/{id}/control`. Injected into worker record streams via filter.

### TopologyChanged

The primary coordination signal. Contains everything workers need to recalculate assignments.

```
kurrent.processors.TopologyChanged {
  activeWorkers: string[]                    // Current active workers (sorted)
  stillActiveTopology: Map<string, int[]>    // What still-active workers owned
  timestamp: Instant
}
```

**Why `stillActiveTopology`?**

Enables sticky assignment strategies. The calculator can see what workers currently own and minimize partition movement.

**Example - Worker C Joins:**

```
Before: A owns [0-63], B owns [64-127]

TopologyChanged {
  activeWorkers: ["A", "B", "C"],
  stillActiveTopology: {
    "A": [0, 1, 2, ..., 63],
    "B": [64, 65, ..., 127]
  }
}

Calculator output: Aâ†’[0-42], Bâ†’[43-85], Câ†’[86-127]
```

### SavepointRequested (Future)

```
kurrent.processors.SavepointRequested {
  savepointId: string
  timestamp: Instant
}
```

---

## 7. Lease Document

Written to `$processors/{id}/leases/{partition}`. Document-sourced (latest event = truth).

```
kurrent.processors.Lease {
  partition: int
  owner: string?              // null = unowned
  checkpoint: long            // log position
  timestamp: Instant          // when last written (checkpoint or ownership change)
  properties: Map<string, string>  // extensible metadata
}
```

**Stream revision IS the version.** No separate version field needed. Optimistic concurrency via expected revision on write.

**Retention:** `$maxCount` set to `CheckpointSettings.RetentionCount` for automatic cleanup.

### When Leases Are Written

Leases are written on **ownership changes** and **checkpoints** only. There are no separate renewal/heartbeat writes (see DR-2).

| Operation | When | What Changes |
|-----------|------|--------------|
| Ownership acquired | Worker acquires partition after TopologyChanged | `owner` set to worker, `timestamp` updated |
| Ownership released | Worker releases partition (graceful handoff) | `owner` set to null, `checkpoint` updated to final position, `timestamp` updated |
| Checkpoint (progress + liveness) | Checkpoint timer fires (regardless of progress) | `checkpoint` updated, `timestamp` updated |
| Forced release (death) | Leader releases dead worker's partitions | `owner` set to null, `checkpoint` preserved, `timestamp` updated |

**Checkpoint timer always fires** — even idle workers write the same position with a fresh timestamp. This is how the leader derives liveness without separate heartbeat writes.

## 8. ProcessorOptions

Same nested structure as existing `ProcessorSettings`, with modifications for Hive Mind leader-based coordination.

```
ProcessorOptions {
  ProcessorId: string
  WorkerId: string                    // Worker-local, not stored
  MaxParallelism: int
  
  Subscription: SubscriptionSettings  // Unchanged
    FilterExpression: string
    Filter: IRecordFilter
    PartitionKeySelector: string
    InitialPosition: SubscriptionInitialPosition
  
  Batching: BatchingSettings          // Unchanged, worker-local
    MaxSize: int
    Timeout: long
  
  Checkpoint: CheckpointSettings      // Modified
    Interval: long                    // Also serves as liveness signal cadency (DR-2)
    RetentionCount: int               // Kept — maps to $maxCount
    // REMOVED: Timeout (gateway concept)
    // DEFERRED: FailurePolicy (see Deferred Items)
  
  Coordination: CoordinationSettings  // Simplified from v0.2
    MissedCheckpointThreshold: int    // How many missed checkpoints = dead
    AssignmentInterval: long          // Leader assignment cycle period
    LeaderTimeout: long               // How long before leader considered dead
  
  Worker: WorkerSettings              // Unchanged, worker-local
    ThrowOnFailure: bool
    BufferCapacity: int
    SkipDecoding: bool
}
```

### Updated: CoordinationSettings

```csharp
/// <summary>
/// Configuration for Hive Mind leader-based coordination.
/// </summary>
public record CoordinationSettings {
    /// <summary>
    /// Number of missed checkpoint intervals before a worker is declared dead.
    /// Detection latency = MissedCheckpointThreshold × CheckpointSettings.Interval.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 2</para>
    /// <para>With default checkpoint interval of 30s, detection latency is ~60s.</para>
    /// <para>For faster detection, reduce checkpoint interval rather than this threshold.</para>
    /// </remarks>
    public int MissedCheckpointThreshold { get; init; } = 2;

    /// <summary>
    /// How often the leader runs the assignment cycle (reads leases, detects failures, rebalances).
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 20000 milliseconds (20 seconds)</para>
    /// <para>Shorter = faster detection but more lease reads. Similar to KCL 3.x leaseAssignmentIntervalMillis.</para>
    /// </remarks>
    public long AssignmentInterval { get; init; } = 20000;

    /// <summary>
    /// Time after which a leader is considered dead if no activity observed.
    /// Other workers may attempt to claim leadership after this timeout.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 60000 milliseconds (60 seconds)</para>
    /// <para>Should be significantly longer than AssignmentInterval.</para>
    /// </remarks>
    public long LeaderTimeout { get; init; } = 60000;
}
```

### What Changed from v0.2

| v0.2 | v0.3 | Why |
|------|------|-----|
| `LeaseTimeout` | **Removed** | Replaced by `MissedCheckpointThreshold × Checkpoint.Interval` (DR-2) |
| `RenewalInterval` | **Removed** | No lease renewal writes — checkpoint IS the liveness signal (DR-2) |
| `HealthCheckInterval` | **Removed** | Leader checks on `AssignmentInterval` cadency, not peer-to-peer (DR-1) |
| — | `MissedCheckpointThreshold` | New — configurable detection sensitivity |
| — | `AssignmentInterval` | New — leader cycle period (KCL 3.x equivalent: `leaseAssignmentIntervalMillis`) |
| — | `LeaderTimeout` | New — leader failure detection |

### What's Stored vs Worker-Local

| Stored (ProcessorCreated) | Worker-Local (not stored) |
|---------------------------|---------------------------|
| ProcessorId | WorkerId |
| MaxParallelism | Batching.* |
| Subscription.* | Worker.* |
| Checkpoint.Interval | |
| Checkpoint.RetentionCount | |
| Coordination.* | |

---

## 9. Leader Election

### Leader Stream

```
$processors/{id}/leader    ($maxCount = 1, document-sourced)

kurrent.processors.LeaderClaim {
  workerId: string
  timestamp: Instant
}
```

### Election Protocol

Leader election uses OCC on the leader stream — the same primitive used for lease ownership. No separate consensus protocol.

```
1. Worker starts, reads $processors/{id}/leader
   → No leader claim, or claim timestamp expired → attempt to claim

2. Write LeaderClaim { workerId: me, timestamp: now }
   with expectedRevision = current revision (or NoStream if first)

3. If OCC succeeds → I am leader. Start assignment cycle.
   If OCC fails → someone else claimed first. Read current leader. Defer.

4. Leader renews claim by writing new LeaderClaim periodically
   (piggybacks on the assignment cycle — each cycle writes a fresh claim)

5. If leader stops renewing (crash), claim timestamp goes stale.
   Other workers detect and compete to claim leadership.
```

### Leader Liveness

The leader's assignment cycle is itself the liveness signal. Each cycle, the leader writes to the control stream (TopologyChanged or a lightweight heartbeat signal). If workers observe no leader activity for `LeaderTimeout`, they challenge for leadership.

Alternatively, since the leader is also a worker processing records, its checkpoint writes also prove it's alive. The leader's own leases going stale indicates both leader and worker failure.

### Leader Failure and Recovery

```
Timeline:
  t=0      A is leader, owns partitions [0-42], B owns [43-85], C owns [86-127]
  t=5s     A's assignment cycle runs normally
  t=7s     A crashes
  
  Processing continues normally for B and C (existing assignments work).
  Only failure detection is delayed — no new topology changes possible.
  
  t=67s    B checks: leader claim timestamp is 60s stale > LeaderTimeout
           B claims leadership: write LeaderClaim { workerId: B }
  t=68s    B runs first assignment cycle as leader
           Reads leases: A's timestamps are stale → A is dead
           B reassigns A's partitions
           Emits TopologyChanged
```

**Key insight:** During leader failover, processing continues for all workers with existing assignments. The only thing delayed is failure detection and rebalancing. This is the same trade-off KCL 3.x makes.

### Leader Self-Healing (Future)

Following KCL 3.x's pattern: if the leader's assignment cycle fails consecutively (`maxConsecutiveFailures` times), it should voluntarily release leadership by not renewing its claim. This handles the "zombie leader" scenario where a worker is alive but unable to perform its duties.

## 10. Worker Lifecycle

### 10.1 Worker Startup

```
1. Read $processors/{id}
   → Get ProcessorOptions (from ProcessorCreated)
   → Check: is processor deleted?

2. Read $processors/{id}/leader
   → Is there a current leader?
   → Is the leader's claim stale (timestamp > LeaderTimeout)?

3. If no leader or stale leader:
   → Attempt to claim leadership (write LeaderClaim with OCC)
   → If won: start leader assignment cycle (see §9)

4. Write WorkerJoined to aggregate:
   → $processors/{id}: WorkerJoined { workerId }
   (Leader sees this via filter injection → triggers immediate assignment)

5. Wait for TopologyChanged signal from leader
   → Contains assignments: which partitions I should own

6. For each assigned partition:
   → Read lease: $processors/{id}/leases/{partition}
   → Write lease: Lease { owner: me, checkpoint: <existing or 0> } with OCC
   → Start processing from checkpoint position

7. Begin checkpoint timer
```

### 10.2 Checkpoint (Progress + Liveness)

```
1. Checkpoint timer fires (always — even if no progress made)

2. Gather current positions for owned partitions

3. Atomic append:
   → $processors/{id}/leases/{p1}: Lease { owner: me, checkpoint: X, timestamp: now }
   → $processors/{id}/leases/{p2}: Lease { owner: me, checkpoint: Y, timestamp: now }
   → ...

4. If OCC conflict on any lease → STOP processing that partition (fenced)

5. NO aggregate event (too noisy)
6. NO signal (nobody needs to react — leader reads timestamps passively)

7. Continue processing
```

**Key:** The checkpoint timer fires regardless of progress. Idle workers write the same checkpoint position with a fresh timestamp. This is how the leader derives liveness (DR-2).

### 10.3 React to TopologyChanged

```
1. Receive TopologyChanged via filter injection (inline with data records)

2. Compare current assignments with new assignments:
   newPartitions = assignments[myWorkerId] - currentlyOwned
   lostPartitions = currentlyOwned - assignments[myWorkerId]

3. For lostPartitions (graceful release):
   → Stop processing partition
   → Checkpoint final position
   → Write lease: Lease { owner: null, checkpoint: <final> }

4. For newPartitions (acquire):
   → Read lease stream to get current revision and checkpoint
   → If owner is null:
       → Write lease: Lease { owner: me } with OCC
       → Start processing from checkpoint
   → If owner is not null (previous worker hasn't released yet):
       → Retry after brief delay (worker is doing graceful handoff)
```

### 10.4 Leave (Graceful)

```
1. Receive shutdown signal

2. Stop consuming new records

3. Finish in-flight work

4. Atomic append:
   → $processors/{id}: WorkerLeft { workerId, reason: Graceful }
   → $processors/{id}/leases/{p1}: Lease { owner: null, checkpoint: <final> }
   → $processors/{id}/leases/{p2}: Lease { owner: null, checkpoint: <final> }
   → ...

5. Exit
```

**Note:** In v0.2, the leaving worker also emitted TopologyChanged. In the leader model, the leader detects the WorkerLeft event and emits the TopologyChanged signal. Workers don't write to the control stream.

### 10.5 Connection Loss (Self-Fencing)

```
1. gRPC stream error received (crash, network failure, server restart)

2. STOP processing immediately — do not wait for checkpoint

3. At-least-once semantics: records since last checkpoint may be reprocessed
   by the next owner. This is expected.

4. If recoverable:
   → Reconnect
   → Restart as new worker (go to §10.1)
```

**Key:** The transport layer provides instant self-fencing (DR-3). No need to check lease revisions or wait for a heartbeat timeout. The worker's own gRPC client tells it something is wrong — trust it.

### 10.6 Reconnect

```
1. Worker starts (same or different workerId)

2. Same as startup (§10.1)
   → Reads state, joins, waits for assignment

3. Leader's assignment cycle accounts for returning workers

4. Resume processing FROM EXISTING CHECKPOINTS
   → At-least-once semantics preserved
```

## 11. Crash Detection & Recovery

### 11.1 Two-Layer Detection Model

Crash detection operates at two layers (DR-3):

| Layer | Who Detects | Mechanism | Latency |
|-------|-------------|-----------|---------|
| **Self-fencing** | The crashed worker itself | gRPC stream error | Instant |
| **Peer detection** | The leader | Stale checkpoint timestamps | MissedCheckpointThreshold × CheckpointInterval |

Self-fencing handles "I know I'm dead." The leader handles "others know I'm dead."

### 11.2 Self-Fencing (Instant)

When a worker's gRPC connection breaks, the client library receives an error immediately. The worker stops processing — no lease check needed, no timeout to wait for.

```
Worker A processing partitions [0-42]
  → gRPC stream error (network failure, server restart, crash)
  → A stops processing immediately
  → Records since last checkpoint may be reprocessed by the next owner
  → If recoverable: A reconnects and rejoins as a new worker
```

### 11.3 Peer Detection by Leader (Checkpoint Staleness)

The leader reads all lease timestamps during each assignment cycle. If a worker's leases haven't been updated within the detection window, the leader declares it dead.

**Detection window:** `MissedCheckpointThreshold × Checkpoint.Interval`

```
Timeline (CheckpointInterval=30s, MissedCheckpointThreshold=2):
  t=0      A checkpoints (timestamp = t=0)
  t=2s     A crashes — gRPC error → A self-fences (instant)
  t=20s    Leader's assignment cycle: A's timestamp is 20s old
           20s < 60s (threshold) → still alive (from leader's perspective)
  t=40s    Leader's assignment cycle: A's timestamp is 40s old
           40s < 60s → still alive
  t=60s    Leader's assignment cycle: A's timestamp is 60s old
           60s >= 60s → DEAD. Leader declares A dead.
```

### 11.4 Death Declaration (Forced Path)

When the leader detects a dead worker, it executes the **forced path** (DR-4):

```
Leader declares A dead. Atomic append:
  → $processors/{id}:             WorkerLeft { workerId: A, reason: Expired }
  → $processors/{id}/leases/0:    Lease { owner: null, checkpoint: <preserved> }
  → $processors/{id}/leases/1:    Lease { owner: null, checkpoint: <preserved> }
  → ...
  → $processors/{id}/control:     TopologyChanged { assignments: { B: [...], C: [...] } }
```

The leader writes leases directly because there's nobody alive to release them. The TopologyChanged signal tells surviving workers to acquire the now-unowned partitions.

### 11.5 Fencing (Zombie Prevention)

**Problem:** Network partition, not crash. A is still running but can't reach KurrentDB.

```
Timeline:
  t=0s    A checkpoints (lease revision = 5)
  t=2s    Network partition — A's gRPC stream breaks → A self-fences (instant)
  
  But what if gRPC keepalive hasn't detected the partition yet?
  
  t=60s   Leader declares A dead, writes leases (revision 6)
  t=65s   A's gRPC stream finally errors → A self-fences
  
  Or: A recovers, tries to checkpoint:
  t=65s   A tries checkpoint (expected revision = 5)
          KurrentDB: "Nope, current revision is 6"
          A: OCC CONFLICT → STOP IMMEDIATELY (fenced)
```

**Two fencing mechanisms:**

1. **Transport fence (primary):** gRPC stream error → stop immediately. Handles most cases.
2. **OCC fence (safety net):** Zombie worker tries to checkpoint → revision mismatch → fenced. Catches edge cases where transport detection is slow.

---

## 12. Rebalancing & Handoff

### 12.1 Graceful Path (Worker Alive)

When the leader rebalances (worker joins, load balancing), living workers handle the transfer:

```
Leader decides: move partitions [64-85] from B to C

1. Leader emits TopologyChanged:
   → assignments: { A: [0-42], B: [43-63], C: [64-127] }

2. Worker B sees signal, compares with current:
   → Losing: [64-85]
   → Stop processing [64-85]
   → Checkpoint final positions
   → Write leases: Lease { owner: null, checkpoint: <final> } for each

3. Worker C sees signal, compares with current:
   → Gaining: [64-85]
   → Read leases — if owner is null: acquire, start processing
   → If owner is not null: B hasn't released yet, retry after brief delay

4. System converges in seconds (B releases, C acquires)
```

**Minimal reprocessing:** B checkpoints before releasing. C starts from B's final position. Only records between B's last periodic checkpoint and the handoff might be reprocessed (if B crashes during handoff).

### 12.2 Forced Path (Worker Dead)

When a worker is dead, the leader handles everything (see §11.4):

```
Leader detects A is dead.
  → Writes A's leases directly: owner → null, checkpoint preserved
  → Emits TopologyChanged with new assignments
  → Surviving workers acquire immediately (leases already released)
```

No waiting for a dead worker to release. No convergence loop. Immediate.

### 12.3 Sticky Assignment

The leader's `IPartitionCalculator` can implement sticky strategies to minimize partition movement:

```
Given:
  current: { A: [0-63], B: [64-127] }
  new worker C joins
  maxParallelism: 128

Naive strategy:
  Divide equally: A→[0-42], B→[43-85], C→[86-127]
  Movement: 21 partitions from A, 21 from B

Sticky strategy:
  Minimize movement from current ownership
  A keeps [0-63], gives [43-63] to C → 21 moved
  B keeps [64-85], gives [86-127] to C → 42 moved
  (Or better: steal evenly from both)
  
  Less churn, faster convergence, less reprocessing
```

## 13. Safety Invariants

### The Core Invariant

**A worker may ONLY process a partition if:**

1. It successfully wrote the lease (acquire or checkpoint)
2. The revision matches its expected revision
3. Its gRPC connection is healthy

**On ANY lease write failure (OCC conflict):**
- STOP processing that partition IMMEDIATELY
- Do not wait, do not retry processing
- Re-read state, re-evaluate

**On ANY gRPC connection error:**
- STOP processing ALL partitions IMMEDIATELY
- Reconnect and rejoin as a new worker

**These are the ONLY ways to prevent split-brain.**

### Two-Layer Fencing

| Layer | Trigger | Scope | Latency |
|-------|---------|-------|---------|
| **Transport** | gRPC stream error | All partitions | Instant |
| **OCC** | Lease write conflict | Single partition | On next checkpoint |

Transport fencing is the primary mechanism. OCC fencing is the safety net for edge cases (zombie workers that haven't detected their own connection failure).

### At-Least-Once Semantics

Records between checkpoints may be reprocessed on failure. This is expected.

```
A checkpointed at position 1000
A processed 1001-1050
A crashed (self-fenced instantly via gRPC error)

Leader detects A dead on next assignment cycle
Leader reassigns A's partitions to B

B takes over, reads checkpoint: 1000
B processes from 1001

Records 1001-1050: processed TWICE
```

For exactly-once: transactional sinks (future work).

### Leader Safety

- Only one leader can exist at a time (OCC on leader stream ensures this)
- Leader writes to dead workers' lease streams are safe because the dead worker will never write again (transport-fenced or OCC-fenced on recovery)
- Leader crash doesn't corrupt state — existing assignments remain valid, only detection is delayed

## 14. Management Operations

### Queries

| Operation | Returns | How |
|-----------|---------|-----|
| `ListProcessors()` | `ProcessorSummary[]` | Read `$processors` category stream |
| `GetProcessor(id)` | `ProcessorDetails` | Read aggregate + all leases |
| `GetProcessorOptions(id)` | `ProcessorOptions` | Read ProcessorCreated from aggregate |
| `GetProgress(id)` | `ProgressInfo` | Read all leases, derive watermarks |

### ProgressInfo

```
ProgressInfo {
  partitions: [
    { partition, owner, checkpoint, lastUpdated },
    ...
  ],
  watermarks: {
    highestCheckpoint: long,    // max(checkpoint) - leading edge
    lowestCheckpoint: long,     // min(checkpoint) - trailing edge
    spread: long                // highest - lowest (skew indicator)
  }
}
```

**Use cases:**
- `lowestCheckpoint`: safe to truncate source up to here
- `spread`: if large, some partitions lagging
- `highestCheckpoint`: how far we've gotten

### Commands

| Operation | Writes To | Notes |
|-----------|-----------|-------|
| `CreateProcessor(options)` | `$processors/{id}`: ProcessorCreated | Returns ProcessorId |
| `DeleteProcessor(id)` | `$processors/{id}`: ProcessorDeleted | Workers detect, stop gracefully |
| `TriggerSavepoint(id)` | `$processors/{id}/control`: SavepointRequested | Returns savepointId |

### Not Implementing

- ~~PauseProcessor~~ - Not needed for Week 1
- ~~ResumeProcessor~~ - Not needed for Week 1

---

## 15. Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MaxParallelism` | 128 | Number of partitions (1-32768) |
| `Checkpoint.Interval` | 30s | Time between checkpoints. Also determines liveness detection speed (DR-2). |
| `Checkpoint.RetentionCount` | 3 | Lease stream `$maxCount` |
| `Coordination.MissedCheckpointThreshold` | 2 | Missed checkpoints before worker declared dead |
| `Coordination.AssignmentInterval` | 20s | Leader assignment cycle period |
| `Coordination.LeaderTimeout` | 60s | Time before leader considered dead |

**Derived Values:**
- **Worker death detection latency** = `MissedCheckpointThreshold × Checkpoint.Interval` (default: 60s)
- **Leader failover time** = `LeaderTimeout + AssignmentInterval` (default: ~80s worst case)

**Constraints:**
- `AssignmentInterval` should be shorter than `MissedCheckpointThreshold × Checkpoint.Interval` so the leader can detect deaths within one detection window
- `LeaderTimeout` should be significantly longer than `AssignmentInterval` to avoid false leader elections

**Tuning for faster failover:**
- Reduce `Checkpoint.Interval` (more writes, faster detection)
- Reduce `AssignmentInterval` (more leader reads, faster reaction)
- Keep `MissedCheckpointThreshold` at 2-3 to avoid false positives from transient delays

## 16. Next Steps

### 16.1 Interfaces

```
□ ICoordinationStore
  - ReadProcessorOptions(processorId) → ProcessorOptions
  - ReadLeases(processorId) → Lease[]
  - ReadLease(processorId, partition) → Lease
  - WriteLease(processorId, lease, expectedRevision) → Result
  - WriteLeases(processorId, leases[], expectedRevisions[]) → Result
  - WriteTopologyChanged(processorId, signal, aggregateEvent?, leases[]) → Result
  - ReadLeaderClaim(processorId) → LeaderClaim?
  - WriteLeaderClaim(processorId, claim, expectedRevision) → Result
  - Atomic multi-stream append support

□ IPartitionCalculator
  - Calculate(activeWorkers, currentAssignment, maxParallelism)
      → Map<workerId, partitions[]>

□ ILeaderLoop
  - RunAssignmentCycle() → Result
  - DetectDeadWorkers(leases, threshold) → WorkerId[]
  - ReactToWorkerJoined(workerId) → void  (trigger immediate cycle)

□ IProcessorManager (management operations)
  - CreateProcessor(options) → ProcessorId
  - DeleteProcessor(processorId) → Result
  - TriggerSavepoint(processorId) → SavepointId
  - ListProcessors() → ProcessorSummary[]
  - GetProcessor(processorId) → ProcessorDetails
  - GetProcessorOptions(processorId) → ProcessorOptions
  - GetProgress(processorId) → ProgressInfo
```

### 16.2 Implementation Order

1. **Types** — Value objects, events, signals, LeaderClaim
2. **ICoordinationStore** — Read/write operations (including leader stream)
3. **Leader election** — Claim/renew/detect stale leader
4. **IPartitionCalculator** — Assignment algorithm
5. **Leader loop** — Assignment cycle, death detection, TopologyChanged emission
6. **Worker loop** — Bootstrap, join, react to signals, checkpoint, self-fence
7. **Management** — Create, delete, query operations
8. **Integration** — Filter injection, wire up with RecordBatch flow

## 17. Deferred Items

### CheckpointSettings.FailurePolicy

**Original purpose (Gateway model):** What to do when a worker doesn't acknowledge a checkpoint within timeout.

**Problem in Hive Mind:** No coordinator waiting for acks. Workers checkpoint independently.

**Questions to resolve:**
- What does "checkpoint failure" mean in Hive Mind?
- Is it just "lease expired" (which we already handle)?
- Do we need a different concept?

**Decision:** Defer until we have more clarity on failure modes.

---

## 18. Future Considerations

### Not in Week 1

- **Flapping prevention** — Exponential backoff on rejoin, probation periods
- **Coordinated checkpoints** — `SavepointRequested` signal implementation
- **Leader self-healing** — Voluntary leadership release after N consecutive failures
- **CPU-aware balancing** — Like KCL 3.x, balance by worker utilization not just partition count

### Potential Optimizations

- **Batch lease reads** — Single query for all partitions (leader already does this)
- **Signal deduplication** — Ignore redundant topology changes
- **Incremental assignment** — Leader only reassigns changed partitions, not full recalculation
- **Worker metrics stream** — Workers report CPU/throughput for smarter balancing

### Server-Side Enhancements (Future)

If/when server can help (without coordination logic):

- Connection lifecycle events (instant peer detection — eliminates checkpoint staleness gap)
- Timer-based barrier injection
- Server time markers (clock sync)
- Subscriber count hints

## Appendix: Atomic Append Patterns

### Worker Joins (v0.3 — Leader Model)

```
Worker writes:
  $processors/{id}:              WorkerJoined { workerId: C }

Leader sees join via filter injection, runs assignment cycle.

Leader writes:
  $processors/{id}/control:      TopologyChanged { assignments: { A: [...], B: [...], C: [...] } }

Worker C then acquires its assigned partitions individually:
  $processors/{id}/leases/86:    Lease { owner: C, checkpoint: X }
  $processors/{id}/leases/87:    Lease { owner: C, checkpoint: X }
  ...
```

### Worker Checkpoints (Unchanged)

```
Atomic append:
  $processors/{id}/leases/0:     Lease { owner: A, checkpoint: 2000, timestamp: now }
  $processors/{id}/leases/1:     Lease { owner: A, checkpoint: 2100, timestamp: now }
  ...

NO aggregate event, NO signal
(Leader reads timestamps passively on its assignment cycle)
```

### Worker Leaves (Graceful — v0.3)

```
Worker writes:
  $processors/{id}:              WorkerLeft { workerId: B, reason: Graceful }
  $processors/{id}/leases/43:    Lease { owner: null, checkpoint: X }
  $processors/{id}/leases/44:    Lease { owner: null, checkpoint: X }
  ...

Leader sees WorkerLeft, runs assignment cycle.
Leader writes:
  $processors/{id}/control:      TopologyChanged { assignments: { A: [...], C: [...] } }
```

**Note:** In v0.3, the leaving worker does NOT write TopologyChanged — that's the leader's job.

### Worker Detected Dead (v0.3 — Leader Forced Path)

```
Leader detects A's leases are stale. Leader writes atomic append:
  $processors/{id}:              WorkerLeft { workerId: A, reason: Expired }
  $processors/{id}/leases/0:     Lease { owner: null, checkpoint: X }
  $processors/{id}/leases/1:     Lease { owner: null, checkpoint: X }
  ...
  $processors/{id}/control:      TopologyChanged { assignments: { B: [...], C: [...] } }
```

### Leader Election

```
Worker writes (OCC):
  $processors/{id}/leader:       LeaderClaim { workerId: B, timestamp: now }
```

---

## Appendix: Comparison of Coordination Models

| Aspect | Gateway (Server-Side) | Hive Mind v0.2 (Decentralized) | Hive Mind v0.3 (Leader-Based) |
|--------|-----------------------|-------------------------------|-------------------------------|
| Server changes | Required | **NONE** | **NONE** |
| Failure detection | Instant (TCP) | 30s timeout (per-partition renewal) | Instant (self) + checkpoint staleness (peers) |
| Heartbeat overhead | Zero (connection) | Per-partition renewal writes | **Zero** (checkpoint IS heartbeat) |
| Coordination logic | Gateway + Workers | Workers only | Leader + Workers |
| Single point failure | Gateway (needs HA) | None | Leader (brief gap, auto-recovery) |
| Assignment model | Gateway assigns | Every worker calculates | Leader assigns |
| Notification model | Push (gateway → worker) | Push (filter injection) | Push (filter injection) |
| OCC races | None (single writer) | On lease acquisition | **None** (leader signals, workers acquire uncontested) |
| Graceful handoff | Yes | No | **Yes** (two-phase) |
| Write amplification | Low | High (N×P renewals) | **Low** (checkpoint only) |
| Ship today | No | Yes | **Yes** |

**Decision:** Hive Mind v0.3 (Leader-Based) combines the best properties: ships without server changes, minimal write amplification, graceful handoff, and globally optimal assignments.
