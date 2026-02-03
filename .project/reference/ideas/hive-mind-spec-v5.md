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
| 0.4.0 | 2026-02-01 | **Consistency review and fixes.** TopologyChanged carries computed assignments (DR-6). Latest-wins semantics for concurrent signals (DR-7). Two-loop leader architecture (worker loop + coordination loop). MSA all-or-nothing checkpoint semantics. Fixed detection latency formulas (+AssignmentInterval). Workers poll leader claim for liveness (removed heartbeat concept). Added bootstrap sequence (§10.0). Added acquisition retry bounds, join batching, MSA failure recovery. OCC on control stream writes. |

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
- **Two-loop leader** — Leader runs a worker loop (data processing) and a coordination loop (assignment/detection) independently

### What Workers Do

1. Connect via gRPC streaming subscription (session)
2. Process records and checkpoint progress at regular intervals
3. React to `TopologyChanged` signals from the leader (via filter injection)
4. Acquire and release partitions as directed by the leader
5. Self-fence on connection failure (stop immediately)
6. Poll the leader claim stream to detect stale leadership

### What The Leader Does (One Elected Worker)

1. Run a separate coordination loop subscribing to the aggregate stream
2. Run assignment cycle periodically (~20s)
3. Read all leases — derive worker liveness from checkpoint timestamps
4. Detect dead workers (stale timestamps) — reassign their partitions
5. React to `WorkerJoined` events (via coordination loop) — trigger debounced assignment
6. Emit `TopologyChanged` signals to the control stream (with OCC)
7. Also process records as a regular worker (via its worker loop)


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

**Detection formula:** `MissedCheckpointThreshold × CheckpointInterval + AssignmentInterval` (worst case). With threshold=2, interval=30s, and assignment interval=20s, worst-case detection is ~80s. The `+ AssignmentInterval` accounts for the leader's assignment cycle alignment — the leader may have just finished checking right before the staleness threshold is crossed.

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

KurrentDB has filter injection: the leader's coordination loop subscribes to the aggregate stream. When a worker writes WorkerJoined, the leader sees it immediately.

**Decision:** New workers write `WorkerJoined` to the aggregate stream. The leader's coordination loop subscribes to this stream. Leader sees the join event and triggers a debounced out-of-cycle assignment, eliminating the polling delay.

**Alternatives Considered:**

| Option | Description | Join Latency |
|--------|-------------|--------------|
| **A. Wait for next assignment cycle** (KCL 3.x) | Worker joins, waits up to 20s for leader's periodic cycle | Up to 20s |
| **B. Worker self-assigns on join** (v0.2) | Worker reads state, calculates, writes leases + signal | Immediate, but two code paths (join vs steady-state) |
| **C. Leader reacts to join event** (chosen) | Worker writes WorkerJoined, leader sees via coordination loop, triggers debounced cycle | Seconds (leader compute + write time) |

**Why C:** Leverages our unique advantage over KCL (push via subscription vs poll). Single assignment code path (leader always assigns). Fast join without self-assignment complexity. Debouncing prevents rebalancing storms when multiple workers join rapidly.

---

### DR-6: TopologyChanged Carries Computed Assignments

**Status:** Accepted

**Context:** v0.3.1 had an inconsistency: §6 defined TopologyChanged with `activeWorkers` and `stillActiveTopology` (input to a partition calculator), while §10-12 and the Appendix referenced `assignments` (output of the calculator). The component model stated that `PartitionCalculator` is used by `AssignmentManager` (leader-only), meaning workers should not compute assignments locally.

**Decision:** TopologyChanged carries the leader's computed `assignments: Map<string, int[]>`. Workers consume the assignment map directly — they do not run any partition calculation logic.

**Alternatives Considered:**

| Option | Description | Pros | Cons |
|--------|-------------|------|------|
| **A. Workers compute locally** (v0.3.1 §6) | Signal contains topology info, workers run PartitionCalculator | Workers can validate leader's decision. Resilient to signal staleness. | Requires identical, deterministic calculator on every worker. Algorithm drift risk. Two code paths. |
| **B. Leader includes assignments** (chosen) | Signal contains final `assignments: Map<string, int[]>` | Single source of truth. Workers are stateless consumers. One code path. | Workers trust the leader's computation entirely. |

**Why B:** The leader already has the full picture (all leases, all worker state). Duplicating the calculation on every worker adds complexity with no safety benefit — if the leader's calculation is wrong, workers computing the same wrong result doesn't help. Single writer, single source of truth.

---

### DR-7: Latest-Wins Semantics for Concurrent Signals

**Status:** Accepted

**Context:** Workers receive TopologyChanged signals inline with data records via filter injection. If the leader emits TC₂ while a worker is still processing TC₁ (e.g., mid-acquisition of newly assigned partitions), the worker needs clear semantics for which signal takes precedence.

**Decision:** Latest-wins. The control stream's revision serves as an implicit epoch — events appear in a monotonically increasing order. When a worker receives a newer TopologyChanged, it abandons in-progress operations from any previous signal and applies the latest assignments.

**Key properties:**
- The leader writes to the control stream with `expectedRevision`, ensuring natural ordering and preventing stale leaders from writing.
- Workers process TopologyChanged events in subscription order — the latest event in the stream always supersedes earlier ones.
- No explicit `epoch` field is needed in the signal — the stream revision itself is the epoch.
- Workers that miss intermediate signals (e.g., subscription starts between TC₁ and TC₂) correctly apply TC₂ since it contains the full assignment map.

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
│    (leader signals, injected into worker streams via filter)               │
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
  │ │WkrLp│ │       │ │WkrLp│ │       │ │WkrLp│ │   Worker loop:
  │ │Ckpt │ │       │ │Ckpt │ │       │ │Ckpt │ │   data + control
  │ │React│ │       │ │React│ │       │ │React│ │
  │ ├─────┤ │       │ └─────┘ │       │ └─────┘ │
  │ │CoLp │ │       │         │       │         │   Coordination loop:
  │ │Assgn│ │       │         │       │         │   leader-only
  │ │Dtect│ │       │         │       │         │
  │ └─────┘ │       │         │       │         │
  └─────────┘       └─────────┘       └─────────┘

  One leader (two loops). Workers react. KurrentDB is dumb storage.
```

### Role Differentiation

All workers share a common base (process records, checkpoint, react to signals). The leader runs additional logic via a **separate coordination loop**:

| Responsibility | Leader | Non-Leader Workers |
|----------------|--------|-------------------|
| Process records (worker loop) | ✅ | ✅ |
| Checkpoint — lease writes (worker loop) | ✅ | ✅ |
| Self-fence on connection loss | ✅ | ✅ |
| React to TopologyChanged (worker loop) | ✅ | ✅ |
| Poll leader claim for liveness | ✅ | ✅ |
| Coordination loop (aggregate stream) | ✅ | ❌ |
| Assignment cycle (~20s) | ✅ | ❌ |
| Death detection (stale leases) | ✅ | ❌ |
| Leader election participation | ✅ | ✅ |

### Two-Loop Leader Architecture

The leader runs two independent subscriptions:

**Worker Loop** (same as every worker):
- Subscribes to `$all` with filter injection
- Filter: user data filter `OR stream_id = '$processors/{id}/control'`
- Processes records, reacts to TopologyChanged signals, checkpoints

**Coordination Loop** (leader-only):
- Subscribes to `$processors/{id}` (the aggregate stream) directly
- Lightweight, low-volume stream (only lifecycle events)
- Detects WorkerJoined/WorkerLeft events
- Triggers debounced assignment cycles

These two loops are independent. The worker loop can fall behind on data processing without affecting the coordination loop's ability to detect worker joins/leaves and manage assignments.

### Filter Injection (Worker Loop)

All workers (including the leader) subscribe to user data AND control signals in a single stream:

```sql
-- User filter
(schema_name IN ('OrderCreated', 'OrderShipped') AND stream_id LIKE 'orders-%')

-- Injected (control signals from leader)
OR stream_id = '$processors/my-processor/control'
```

Workers see `TopologyChanged` signals inline with records at deterministic log positions.

### Components

> **Note:** This section captures the component model as currently understood. Needs further refinement — interfaces, ownership boundaries, and error propagation are not fully worked out yet.

#### Component Overview

```
ProcessorWorker (top-level orchestrator per worker)
│
├── LeaderElector              ← ALL workers
│   └── Claims/renews/detects stale leadership via OCC
│   └── Non-leaders poll leader claim every LeaderTimeout / 3
│
├── CheckpointManager          ← ALL workers
│   └── Timer-based checkpoint writes (progress + liveness)
│   └── Uses MSA (multi-stream append) — all-or-nothing
│   └── Detects OCC conflicts (fencing)
│
├── AssignmentManager          ← LEADER ONLY (coordination loop)
│   └── Periodic assignment cycle (read leases, detect deaths, compute)
│   └── Emits TopologyChanged to control stream (with expectedRevision)
│   └── Forced path: writes leases for dead workers via MSA
│   └── Reacts to WorkerJoined for debounced out-of-cycle assignment
│
├── PartitionCalculator        ← Used by AssignmentManager
│   └── Pure computation: workers + current assignment → new assignment map
│   └── Pluggable strategy (naive, sticky, future: CPU-aware)
│
└── CoordinationStore          ← Used by all components
    └── Persistence port over KurrentDB streams
    └── Read/write leases, leader claims, aggregate events, control signals
    └── Encapsulates stream naming, MSA, expected revisions
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
| **LeaseDiscovery** | **Eliminated (DR-5, DR-6)** | KCL polls GSI to find assignments. We push via TopologyChanged (filter injection). |
| **WorkerMetricStatsReporter** | **Not needed (v1)** | KCL uses CPU metrics for balancing. We start with partition-count balancing. |
| *(DynamoDB — 3 tables)* | CoordinationStore | One system (KurrentDB streams) instead of three DynamoDB tables |
| *(no equivalent)* | CheckpointManager | KCL separates checkpoint and renewal. We unified them into one timer. |

#### What We Eliminated (and Why)

**LeaseRenewer** — KCL's most active component. Runs every ~3.3s, writes a counter increment per lease. With 100 partitions per worker, that's ~30 writes/s just for liveness. Eliminated because checkpoint writes already carry timestamps (DR-2).

**LeaseDiscovery** — KCL workers poll a DynamoDB GSI every cycle to discover "did the leader assign me something new?" Eliminated because TopologyChanged signals arrive inline with data via filter injection (DR-5, DR-6). Zero polling latency.

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

- **Aggregate stream**: Single source of truth for processor lifecycle. Event-sourced for full audit trail. Leader subscribes to this via its coordination loop to detect `WorkerJoined` events instantly.
- **Control stream**: Separate from aggregate for filter injection. Workers see `TopologyChanged` signals inline with their data records — push, not poll. Leader writes with `expectedRevision` for natural ordering and stale-leader prevention.
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
    missedCheckpointThreshold: int
    assignmentInterval: long
    leaderTimeout: long
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
- `Expired` - Worker's lease expired (detected dead by leader)
- `Fenced` - Worker was fenced out (zombie detection)

---

## 6. Control Signals

Written to `$processors/{id}/control`. Injected into worker record streams via filter. Leader writes with `expectedRevision` (see DR-7).

### TopologyChanged

The primary coordination signal. Contains the leader's computed assignment map — everything workers need to know about which partitions they own.

```
kurrent.processors.TopologyChanged {
  assignments: Map<string, int[]>    // workerId → partition numbers
  timestamp: Instant
}
```

The control stream's revision serves as an implicit epoch (DR-7). Workers process signals in subscription order — the latest signal always supersedes earlier ones.

**Example — Worker C Joins:**

```
Before: A owns [0-63], B owns [64-127]

Leader's PartitionCalculator computes new assignments:
  A → [0-42], B → [43-85], C → [86-127]

Leader writes to control stream (expectedRevision = current):
TopologyChanged {
  assignments: {
    "A": [0, 1, 2, ..., 42],
    "B": [43, 44, ..., 85],
    "C": [86, 87, ..., 127]
  }
}
```

Workers A and B compare their current partitions with the new assignments. A releases [43-63], B releases [64-85]. Worker C acquires [86-127].

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

**First lease creation:** When a partition has never been leased (stream does not exist), the first write uses `ExpectedRevision.NoStream`. The initial `checkpoint` value is determined by `SubscriptionSettings.InitialPosition`: `Earliest` → position 0, `Latest` → current end of `$all`.

**Shorthand notation:** Throughout this spec, lease writes are shown in abbreviated form (e.g., `{ owner: null, checkpoint: X }`). All writes include the full Lease document — only the conceptually relevant fields are shown.

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
    Interval: long                    // Also serves as liveness signal cadence (DR-2)
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
    /// Worst-case detection latency = MissedCheckpointThreshold × CheckpointSettings.Interval + AssignmentInterval.
    /// </summary>
    /// <remarks>
    /// <para><b>Default:</b> 2</para>
    /// <para>With default checkpoint interval of 30s and assignment interval of 20s,
    /// worst-case detection latency is ~80s.</para>
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
    /// <para>Workers poll the leader claim stream every LeaderTimeout / 3 (~20s).</para>
    /// </remarks>
    public long LeaderTimeout { get; init; } = 60000;
}
```

### What Changed from v0.2

| v0.2 | v0.3+ | Why |
|------|-------|-----|
| `LeaseTimeout` | **Removed** | Replaced by `MissedCheckpointThreshold × Checkpoint.Interval + AssignmentInterval` (DR-2) |
| `RenewalInterval` | **Removed** | No lease renewal writes — checkpoint IS the liveness signal (DR-2) |
| `HealthCheckInterval` | **Removed** | Leader checks on `AssignmentInterval` cadence, not peer-to-peer (DR-1) |
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

3. If OCC succeeds → I am leader. Start coordination loop + assignment cycle.
   If OCC fails → someone else claimed first. Read current leader. Defer.

4. Leader renews claim by writing new LeaderClaim periodically
   (piggybacks on the assignment cycle — each cycle writes a fresh claim)
   Uses expectedRevision from last write. OCC failure = lost leadership.

5. If leader stops renewing (crash), claim timestamp goes stale.
   Other workers detect and compete to claim leadership.
```

### Leader Liveness Detection (Non-Leader Workers)

Non-leader workers poll the leader claim stream periodically to detect stale leadership:

**Polling interval:** `LeaderTimeout / 3` (default: ~20s). This provides ~3 checks per timeout window.

```
1. Worker reads $processors/{id}/leader every LeaderTimeout / 3

2. If claim.timestamp is older than LeaderTimeout:
   → Leader is considered dead
   → Worker attempts to claim leadership (step 2 of Election Protocol)

3. If claim is fresh:
   → Leader is alive, continue as non-leader
```

### Leader Failure and Recovery

```
Timeline:
  t=0      A is leader, owns partitions [0-42], B owns [43-85], C owns [86-127]
  t=5s     A's assignment cycle runs normally, renews claim
  t=7s     A crashes

  Processing continues normally for B and C (existing assignments work).
  Only failure detection is delayed — no new topology changes possible.

  t=27s    B polls leader claim: timestamp is 22s old < 60s → still alive
  t=47s    B polls leader claim: timestamp is 42s old < 60s → still alive
  t=67s    B polls leader claim: timestamp is 62s old > LeaderTimeout (60s)
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

### 10.0 Bootstrap Sequence (Cold Start)

When a brand-new processor starts with no existing state:

```
1. First worker reads $processors/{id}
   → Stream does not exist, or no ProcessorCreated event
   → Write ProcessorCreated { maxParallelism, subscription, checkpoint, coordination }

2. Read $processors/{id}/leader
   → Stream does not exist → no leader

3. Claim leadership:
   → Write LeaderClaim { workerId: me } with ExpectedRevision.NoStream
   → Success: I am the first leader

4. Write WorkerJoined { workerId: me } to $processors/{id}

5. Short-circuit: run first assignment cycle immediately
   → Read ProcessorCreated.maxParallelism → partition set = [0, maxParallelism)
   → No lease streams exist yet → all partitions unassigned
   → Compute assignments: all partitions → me (only worker)
   → Write TopologyChanged { assignments: { me: [0..N-1] } }
     to control stream with ExpectedRevision.NoStream

6. Start coordination loop (subscribe to aggregate stream)

7. Start worker loop (subscribe to $all with filter injection)
   → Worker sees TopologyChanged from step 5
   → Acquires all partitions (see §10.1 step 6)

8. Begin checkpoint timer
```

**Partition initialization:** The first time a partition is acquired, its lease stream does not exist. The worker creates it using `ExpectedRevision.NoStream`, with `checkpoint` set based on `SubscriptionSettings.InitialPosition` (`Earliest` → position 0, `Latest` → current end of `$all`).

**Subsequent workers** follow the normal startup sequence (§10.1). They write WorkerJoined, the leader sees it via the coordination loop, and triggers a debounced assignment cycle.

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
   → If won: run bootstrap/first assignment cycle (see §10.0 step 5)
   → Start coordination loop

4. Write WorkerJoined to aggregate:
   → $processors/{id}: WorkerJoined { workerId }
   (Leader sees this via coordination loop → triggers debounced assignment)

5. Start worker loop (subscribe to $all with filter injection)

6. Wait for TopologyChanged signal via worker loop
   → Contains assignments: which partitions I should own

7. For each assigned partition:
   → Read lease: $processors/{id}/leases/{partition}
   → If stream does not exist: use ExpectedRevision.NoStream
   → If stream exists: use current stream revision
   → Write lease: Lease { owner: me, checkpoint: <existing or initial> } with OCC
   → Start processing from checkpoint position

8. Begin checkpoint timer

9. Begin polling leader claim stream (every LeaderTimeout / 3)
```

**WorkerJoined idempotency:** If a worker crashes after writing WorkerJoined but before receiving TopologyChanged, it restarts and writes WorkerJoined again. The leader deduplicates by workerId when computing active workers — multiple WorkerJoined events for the same workerId are treated as one active worker.

### 10.2 Checkpoint (Progress + Liveness)

```
1. Checkpoint timer fires (always — even if no progress made)

2. Gather current positions for owned partitions

3. MSA (multi-stream append) — all-or-nothing:
   → $processors/{id}/leases/{p1}: Lease { owner: me, checkpoint: X, timestamp: now }
   → $processors/{id}/leases/{p2}: Lease { owner: me, checkpoint: Y, timestamp: now }
   → ...
   Each stream write uses expectedRevision from last known write.

4. If MSA succeeds → checkpoint complete. Continue processing.

5. If MSA fails (OCC conflict on any lease):
   → The ENTIRE MSA fails (all-or-nothing)
   → At least one partition was fenced (another writer changed the lease)
   → Re-read ALL lease streams for owned partitions
   → For each lease: if owner is still me → keep processing
   → For each lease: if owner changed → STOP processing that partition (fenced)
   → Retry checkpoint for partitions still owned

6. NO aggregate event (too noisy)
7. NO signal (nobody needs to react — leader reads timestamps passively)
```

**Key:** The checkpoint timer fires regardless of progress. Idle workers write the same checkpoint position with a fresh timestamp. This is how the leader derives liveness (DR-2).

### 10.3 React to TopologyChanged

```
1. Receive TopologyChanged via filter injection (inline with data records)

2. Apply latest-wins (DR-7):
   → If currently processing a previous TopologyChanged (mid-acquisition/release),
     abandon that in-progress work and apply this newer signal instead.

3. Compare current assignments with new assignments:
   myAssignment = assignments[myWorkerId]    // may be empty or absent
   newPartitions = myAssignment - currentlyOwned
   lostPartitions = currentlyOwned - myAssignment

4. For lostPartitions (graceful release):
   → Stop processing partition
   → Checkpoint final position
   → Write lease: Lease { owner: null, checkpoint: <final>, timestamp: now }

5. For newPartitions (acquire):
   → Read lease stream to get current revision and checkpoint
   → If stream does not exist: use ExpectedRevision.NoStream
   → If owner is null:
       → Write lease: Lease { owner: me } with OCC
       → Start processing from checkpoint
   → If owner is not null (previous worker hasn't released yet):
       → Retry with backoff (see Acquisition Retry below)
```

**Acquisition Retry:**
When a lease still has an owner (graceful handoff in progress), the acquiring worker retries with exponential backoff. The retry budget is `2 × CheckpointInterval` (default: 60s). If the budget is exhausted — the previous owner should have released within this window — the worker abandons the acquisition. The leader's next assignment cycle will re-evaluate and handle it (potentially via the forced path if the previous owner is dead).

### 10.4 Leave (Graceful)

```
1. Receive shutdown signal

2. Stop consuming new records

3. Finish in-flight work

4. MSA (multi-stream append):
   → $processors/{id}: WorkerLeft { workerId, reason: Graceful }
   → $processors/{id}/leases/{p1}: Lease { owner: null, checkpoint: <final>, timestamp: now }
   → $processors/{id}/leases/{p2}: Lease { owner: null, checkpoint: <final>, timestamp: now }
   → ...

5. If MSA succeeds → exit cleanly.

6. If MSA fails (OCC conflict on any lease):
   → The worker is being fenced or another writer touched a lease.
   → Best effort: exit without releasing.
   → Leader will detect stale leases and handle via forced path.

7. Exit
```

**Note:** In v0.2, the leaving worker also emitted TopologyChanged. In the leader model, the leader detects the WorkerLeft event via its coordination loop and emits the TopologyChanged signal. Workers don't write to the control stream.

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

**Note on workerId:** If a worker reconnects with the same workerId, the leader deduplicates the WorkerJoined event (see §10.1 idempotency note). If a different workerId is used, the old worker's leases will eventually be detected as stale and released via the forced path.

## 11. Crash Detection & Recovery

### 11.1 Two-Layer Detection Model

Crash detection operates at two layers (DR-3):

| Layer | Who Detects | Mechanism | Latency |
|-------|-------------|-----------|---------|
| **Self-fencing** | The crashed worker itself | gRPC stream error | Instant |
| **Peer detection** | The leader | Stale checkpoint timestamps | MissedCheckpointThreshold × CheckpointInterval + AssignmentInterval |

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

**Worst-case detection latency:** `MissedCheckpointThreshold × Checkpoint.Interval + AssignmentInterval` — the leader might have just finished checking right before the staleness threshold is crossed, adding up to one AssignmentInterval of delay.

```
Timeline (CheckpointInterval=30s, MissedCheckpointThreshold=2, AssignmentInterval=20s):
  t=0      A checkpoints (timestamp = t=0)
  t=2s     A crashes — gRPC error → A self-fences (instant)

  Leader's assignment cycles run every 20s:
  t=20s    Leader checks: A's timestamp is 20s old
           20s < 60s (threshold) → still alive (from leader's perspective)
  t=40s    Leader checks: A's timestamp is 40s old
           40s < 60s → still alive
  t=60s    Leader checks: A's timestamp is 60s old
           60s >= 60s → DEAD. Leader declares A dead.

  Detection latency from crash: 58s (t=60 - t=2)

  Worst case: A crashes at t=0.001 (right after checkpoint + leader check)
  Next checks: t=20, t=40, t=60 → detected at t=60. Latency: ~60s.

  Even worse alignment: A checkpoints at t=0, leader checks at t=0.001,
  A crashes at t=0.002. Next check where staleness ≥ 60s: t=80.
  Latency: ~80s. (This is the true worst case.)
```

### 11.4 Death Declaration (Forced Path)

When the leader detects a dead worker, it executes the **forced path** (DR-4):

```
Leader declares A dead. MSA (multi-stream append):
  → $processors/{id}:             WorkerLeft { workerId: A, reason: Expired }
  → $processors/{id}/leases/0:    Lease { owner: null, checkpoint: <preserved>, timestamp: now }
  → $processors/{id}/leases/1:    Lease { owner: null, checkpoint: <preserved>, timestamp: now }
  → ...
  → $processors/{id}/control:     TopologyChanged { assignments: { B: [...], C: [...] } }
     (with expectedRevision on control stream)
```

The leader writes leases directly because there's nobody alive to release them. The TopologyChanged signal tells surviving workers to acquire the now-unowned partitions.

**MSA failure recovery:** If the MSA fails (OCC conflict on any lease — e.g., the "dead" worker actually just checkpointed), the leader re-reads all leases, re-evaluates which workers are dead, and retries the assignment cycle.

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
   (written with expectedRevision on control stream)

2. Worker B sees signal, compares with current:
   → Losing: [64-85]
   → Stop processing [64-85]
   → Checkpoint final positions
   → Write leases: Lease { owner: null, checkpoint: <final>, timestamp: now } for each

3. Worker C sees signal, compares with current:
   → Gaining: [64-85]
   → Read leases — if owner is null: acquire, start processing
   → If owner is not null: B hasn't released yet, retry with backoff (§10.3)

4. System converges in seconds (B releases, C acquires)
```

**Minimal reprocessing:** B checkpoints before releasing. C starts from B's final position. Only records between B's last periodic checkpoint and the handoff might be reprocessed (if B crashes during handoff).

### 12.2 Forced Path (Worker Dead)

When a worker is dead, the leader handles everything (see §11.4):

```
Leader detects A is dead.
  → Writes A's leases directly: owner → null, checkpoint preserved
  → Emits TopologyChanged with new assignments (via MSA)
  → Surviving workers acquire immediately (leases already released)
```

No waiting for a dead worker to release. No convergence loop. Immediate.

### 12.3 Leader Self-Rebalancing

The leader is also a worker. When it rebalances partitions away from itself:

1. Leader computes new assignments (which reduce its own partition count)
2. Leader emits TopologyChanged to the control stream
3. Leader's worker loop sees TopologyChanged via filter injection
4. Leader reacts through the **same code path** as any other worker — releasing lost partitions, acquiring gained partitions

The leader does NOT short-circuit this flow during steady state. One code path for all workers prevents behavioral divergence. The latency of seeing its own signal (one write + read from control stream) is negligible.

**Exception:** The first boot (§10.0) is the only case where the leader short-circuits, because the worker loop hasn't started yet.

### 12.4 Join Batching (Debounce)

When multiple workers join rapidly (DR-5), each WorkerJoined event triggers a coordination loop reaction. Without batching, 10 workers joining in 5 seconds would cause 10 consecutive assignment cycles, each emitting a TopologyChanged — resulting in a rebalancing storm.

**Debounce rule:** After an immediate out-of-cycle assignment, the leader suppresses further immediate triggers for a cooldown period (e.g., `AssignmentInterval / 2`). Rapid joins fold into the next scheduled or cooldown-expired cycle. This balances responsiveness (fast first assignment) with stability (no storm).

### 12.5 Sticky Assignment

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
  Steal evenly from both:
  A keeps [0-42], gives up [43-63]
  B keeps [64-106], gives up [107-127]
  C gets [43-63] + [107-127]

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

### Latest-Wins Invariant (DR-7)

Workers always apply the most recent TopologyChanged signal. If a newer signal arrives while processing an older one, the older signal's operations are abandoned. The control stream's revision provides natural ordering — workers process signals in subscription order.

### Leader Transition Safety

During leader transitions, both old and new leaders may briefly coexist. Safety is maintained by:

1. **OCC on leader claim stream:** Only one leader can successfully renew at a time. A stale leader's renewal fails, informing it that leadership was lost.
2. **OCC on control stream:** The leader writes TopologyChanged with `expectedRevision`. If a stale leader tries to write after a new leader already has, the write fails.
3. **OCC on lease streams (forced path):** If a stale leader tries to force-release leases that have been modified by checkpoints or the new leader, the MSA fails. The stale leader must re-read state.

**Recommendation:** Leaders should re-validate their claim (re-read leader stream) before writing in the forced path as an additional safeguard.

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
- Leader writes to the control stream with `expectedRevision` prevent stale leaders from producing conflicting signals
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
| `DeleteProcessor(id)` | `$processors/{id}`: ProcessorDeleted | **Precondition:** all workers must be disconnected |
| `TriggerSavepoint(id)` | `$processors/{id}/control`: SavepointRequested | Returns savepointId |

### DeleteProcessor Precondition

`DeleteProcessor` requires that all workers for the processor are already disconnected. It is a management-time operation, not a runtime coordination signal. Workers do not need to dynamically detect or react to `ProcessorDeleted` — the operator is responsible for shutting down workers before deleting the processor.

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
- **Worker death detection latency (worst case)** = `MissedCheckpointThreshold × Checkpoint.Interval + AssignmentInterval` (default: 80s)
- **Worker death detection latency (best case)** = `MissedCheckpointThreshold × Checkpoint.Interval` (default: 60s)
- **Leader failover time** = `LeaderTimeout + AssignmentInterval` (default: ~80s worst case)
- **Leader claim polling interval** = `LeaderTimeout / 3` (default: ~20s)

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
  - WriteLeasesMSA(processorId, leases[], expectedRevisions[]) → Result
  - WriteTopologyChanged(processorId, signal, expectedRevision) → Result
  - WriteDeathDeclarationMSA(processorId, workerLeft, leases[], topologyChanged, expectedRevisions) → Result
  - ReadLeaderClaim(processorId) → LeaderClaim?
  - WriteLeaderClaim(processorId, claim, expectedRevision) → Result

□ IPartitionCalculator
  - Calculate(activeWorkers, currentAssignment, maxParallelism)
      → Map<workerId, partitions[]>

□ ILeaderLoop
  - RunAssignmentCycle() → Result
  - DetectDeadWorkers(leases, threshold) → WorkerId[]
  - ReactToWorkerJoined(workerId) → void  (trigger debounced cycle)

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
2. **ICoordinationStore** — Read/write operations (including leader stream, MSA)
3. **Leader election** — Claim/renew/detect stale leader
4. **IPartitionCalculator** — Assignment algorithm
5. **Leader loop** — Assignment cycle, death detection, TopologyChanged emission, join debouncing
6. **Worker loop** — Bootstrap, join, react to signals, checkpoint, self-fence, leader polling
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

### Partition Count Changes (MaxParallelism)

`MaxParallelism` is set at `ProcessorCreated` time and cannot be changed afterward. There is no `UpdateProcessor` operation. Changing the partition count would require:
- A new ProcessorCreated event (or an UpdateProcessor event type)
- A migration strategy for existing lease streams
- Handling the transition period where old and new partition counts overlap

**Decision:** Out of scope for v1. If partition count needs to change, create a new processor and migrate.

### Per-Partition Leases vs Current Single Checkpoint Stream

The current codebase uses a single checkpoint stream (`$processors/{id}/checkpoints`) with all partition positions in one event. This spec defines per-partition lease streams (`$processors/{id}/leases/{partition}`). The per-partition model is required for Hive Mind (independent OCC per partition, no false conflicts between workers). The migration path from the current model is an implementation concern.

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

### Worker Joins (v0.4 — Leader Model)

```
Worker writes:
  $processors/{id}:              WorkerJoined { workerId: C }

Leader sees join via coordination loop, runs debounced assignment cycle.

Leader writes (MSA with expectedRevision on control stream):
  $processors/{id}/control:      TopologyChanged {
                                   assignments: { A: [0-42], B: [43-85], C: [86-127] }
                                 }

Worker C then acquires its assigned partitions individually:
  $processors/{id}/leases/86:    Lease { owner: C, checkpoint: X }
  $processors/{id}/leases/87:    Lease { owner: C, checkpoint: X }
  ...
```

### Worker Checkpoints (MSA — All-or-Nothing)

```
MSA (multi-stream append):
  $processors/{id}/leases/0:     Lease { owner: A, checkpoint: 2000, timestamp: now }
  $processors/{id}/leases/1:     Lease { owner: A, checkpoint: 2100, timestamp: now }
  ...

Each stream write uses expectedRevision from last known write.
If any OCC conflict → entire MSA fails → re-read leases, retry for still-owned.

NO aggregate event, NO signal
(Leader reads timestamps passively on its assignment cycle)
```

### Worker Leaves (Graceful — v0.4)

```
Worker writes (MSA):
  $processors/{id}:              WorkerLeft { workerId: B, reason: Graceful }
  $processors/{id}/leases/43:    Lease { owner: null, checkpoint: X, timestamp: now }
  $processors/{id}/leases/44:    Lease { owner: null, checkpoint: X, timestamp: now }
  ...

If MSA fails: exit without releasing, leader handles via forced path.

Leader sees WorkerLeft via coordination loop, runs assignment cycle.
Leader writes (with expectedRevision on control stream):
  $processors/{id}/control:      TopologyChanged {
                                   assignments: { A: [...], C: [...] }
                                 }
```

**Note:** In v0.4, the leaving worker does NOT write TopologyChanged — that's the leader's job.

### Worker Detected Dead (v0.4 — Leader Forced Path)

```
Leader detects A's leases are stale. Leader writes MSA:
  $processors/{id}:              WorkerLeft { workerId: A, reason: Expired }
  $processors/{id}/leases/0:     Lease { owner: null, checkpoint: X, timestamp: now }
  $processors/{id}/leases/1:     Lease { owner: null, checkpoint: X, timestamp: now }
  ...
  $processors/{id}/control:      TopologyChanged {
                                   assignments: { B: [...], C: [...] }
                                 }
     (with expectedRevision on control stream)

If MSA fails → re-read leases, re-evaluate, retry cycle.
```

### Leader Election

```
Worker writes (OCC):
  $processors/{id}/leader:       LeaderClaim { workerId: B, timestamp: now }
     (with expectedRevision)
```

---

## Appendix: Comparison of Coordination Models

| Aspect | Gateway (Server-Side) | Hive Mind v0.2 (Decentralized) | Hive Mind v0.4 (Leader-Based) |
|--------|-----------------------|-------------------------------|-------------------------------|
| Server changes | Required | **NONE** | **NONE** |
| Failure detection | Instant (TCP) | 30s timeout (per-partition renewal) | Instant (self) + checkpoint staleness (peers) |
| Heartbeat overhead | Zero (connection) | Per-partition renewal writes | **Zero** (checkpoint IS heartbeat) |
| Coordination logic | Gateway + Workers | Workers only | Leader (two loops) + Workers |
| Single point failure | Gateway (needs HA) | None | Leader (brief gap, auto-recovery) |
| Assignment model | Gateway assigns | Every worker calculates | Leader assigns |
| Notification model | Push (gateway → worker) | Push (filter injection) | Push (filter injection) |
| OCC races | None (single writer) | On lease acquisition | **None** (leader signals, workers acquire uncontested) |
| Graceful handoff | Yes | No | **Yes** (two-phase) |
| Write amplification | Low | High (N×P renewals) | **Low** (checkpoint only) |
| Ship today | No | Yes | **Yes** |

**Decision:** Hive Mind v0.4 (Leader-Based) combines the best properties: ships without server changes, minimal write amplification, graceful handoff, and globally optimal assignments.
