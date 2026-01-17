# Kurrent Processors
## Executive Technical Brief

---

| Field         | Value                     |
|---------------|---------------------------|
| **Author**    | Sérgio Silveira           |
| **Team**      | Developer Experience      |
| **Date**      | January 2026              |
| **Status**    | Summit Presentation Draft |

### Version History

| Version | Date       | Changes |
|---------|------------|---------|
| 1.0     | 2026-01-16 | Initial draft |
| 2.0     | 2026-01-16 | Restructured around 6-problem narrative from processors.md |
| 2.1     | 2026-01-16 | Added "Built on Client V2" section with filter expressions, auto SerDe, resilience |
| 2.2     | 2026-01-16 | Clarified Autonomous Mode: State Store enables coordination, records always from KurrentDB |

---

## Built on Client V2: The Foundation

Kurrent Processors is the flagship feature of **KurrentDB .NET Client V2**—a complete reimagining of how developers interact with KurrentDB. V2 provides game-changing capabilities out of the box that make Processors possible.

### New Ubiquitous Language

V2 introduces a deliberate shift in terminology that reflects what the platform actually does:

| V1 Terminology | V2 Terminology | Why |
|----------------|----------------|-----|
| Events | **Records** | We store more than events—telemetry, commands, state snapshots |
| Event Type | **Schema Name** | Aligns with Schema Registry; messages have schemas |
| Payload | **Message** | Records contain messages; the message is what matters |

This isn't just naming—it's a conceptual shift. KurrentDB is a **record store**, not just an event store.

### Client V2 Game Changers

**1. SQL-Based Filter Expressions**

V1 filtering was primitive—stream name OR event type, regex patterns, no combinations. V2 introduces SQL-like filter expressions with DuckDB-compatible syntax:

```sql
-- Complex filtering that was impossible in V1
stream LIKE 'order-%' AND 
properties.region IN ('us-east', 'us-west') AND 
properties.amount > 100 AND
timestamp >= '2024-01-01'
```

Full SQL semantics: `AND`, `OR`, `NOT`, `BETWEEN`, `IN`, `LIKE`, `ILIKE`, `IS NULL`, pattern matching, metadata access. Currently evaluated client-side; roadmap includes server-side optimization.

**2. Automatic Serialization with Schema Registry**

Zero-configuration SerDe for JSON and Protobuf with first-class Schema Registry integration. Write your messages, V2 handles serialization, deserialization, and schema validation automatically.

**3. Built-In Resilience**

Connection resiliency embedded in the client based on gRPC protocol capabilities. No more manual retry logic—the client handles transient failures automatically.

### Core Architectural Principles

| Principle | What It Means |
|-----------|---------------|
| **Type-safe error handling** | Result pattern throughout—no exception-based flow control |
| **Functional influence** | Discriminated unions (IVariant), Result monads, immutability |
| **Zero reflection** | Source generators for AOT compatibility |
| **Protocol versioning** | Dual support for v1 (EventStoreDB) and v2 (KurrentDB native) |

### What Processors Adds

Built *on* V2's foundation, Processors introduces its own architectural patterns:

**Gateway Abstraction (Ports & Adapters)**

The reason Processors can ship without server changes. Worker code talks to `IGateway`, not to the server directly:
- `LegacyGateway`: Simulates full protocol client-side using V1 Streams API
- `GrpcGateway` (future): Real server protocol

Swap gateways—**zero worker code changes**. This is Processors-specific architecture, not a V2 feature.

**IVariant Message Protocol**

Type-safe checkpoint protocol with compile-time exhaustive pattern matching:
```csharp
// Compiler ensures all cases handled
message.Match(
    workOrder => HandleWorkOrder(workOrder),
    recordBatch => HandleBatch(recordBatch),
    barrier => HandleCheckpoint(barrier)
);
```

**Processors proves V2's architecture works.** The patterns V2 established made building Processors possible.

---

## The Problem: A Developer Experience Crisis

Developers building event-driven systems on KurrentDB face a fundamental barrier. The current subscription API—with its proliferation of parameters, manual position tracking, and lack of built-in resilience—forces teams to build their own processing frameworks before they can do anything useful.

This isn't a minor inconvenience. It's a **barrier to adoption** that pushes developers toward competing platforms.

### The Cascade of Consequences

**Lost events and duplicate processing** when developers implement checkpointing incorrectly. Every team discovers the same edge cases the hard way.

**Support burden** answering the same questions repeatedly about handling failures, tracking positions, and recovering from crashes.

**Developer abandonment** as teams give up and migrate to Kafka, Pulsar, Databricks, ClickHouse, or managed stream processing services.

**Custom framework proliferation** where every team reinvents the wheel, each with its own bugs.

The subscription model was designed as a low-level primitive. Users need a high-level abstraction. That gap has become untenable.

### Why Existing Solutions Fall Short

**Internal options** (current KurrentDB subscriptions):
- Require extensive boilerplate for basic reliability
- No coordination between workers
- Position tracking is manual and error-prone
- Scaling requires custom load-balancing logic

**External migrations** (MassTransit, Kafka Streams, Flink):
- Require abandoning KurrentDB or running parallel infrastructure
- Add operational complexity and infrastructure cost
- Lose KurrentDB's event sourcing strengths
- Represent an admission of platform inadequacy

The market offers robust stream processing—but not *on* KurrentDB.

---

## What We Built: Six Problems Solved

Kurrent Processors solves six fundamental problems in distributed stream processing. Each solution builds on the previous, creating a complete system for fault-tolerant, scalable processing.

### Problem 1: Distributing Stateful Work

**The problem:** In stateful processing, records with the same key must go to the same worker. But we want parallelism. If Worker A has the running average for Sensor-1 and Worker B joins, how does B get some of A's state without losing data?

**The solution: Key Groups (Partitions)**

Instead of assigning keys directly to workers, we introduce an intermediate layer: **partitions**—inspired by Flink's "key groups" and Kafka's partitions.

```
Keys → hash(key) % max_parallelism → Partition → Worker
```

**Key properties:**
- **Fixed partition count**: Set at creation (`max_parallelism`), never changes
- **Deterministic mapping**: Same key → same partition (always)
- **Movable partitions**: Partitions move between workers, not individual keys
- **State per partition**: Each partition has its own state blob

Adding Worker C? Move partitions 86-127 from B to C. State follows partition—download partition 86's state, you have all keys that hash to 86.

---

### Problem 2: Recovering from Failures

**The problem:** Worker A is processing records 1000-2000 when it crashes at record 1500. Did records 1000-1500 update external systems? What state did Worker A have? How do we resume without losing or duplicating work?

**The solution: Coordinated Checkpoints**

Periodically, all workers snapshot their state and progress at a **consistent point**. On recovery, restore from the last checkpoint.

```
Log:    [1000]---[1250]---[1500]---[1750]---[2000]
                    ↓                          ↓
              Checkpoint 1500            Checkpoint 2000
                    
Worker crashes at 1750 → Restore from 1500 → Replay 1500-1750
```

**What gets checkpointed:**
- Position in the log (per partition)
- Worker state (per partition)
- Metadata (timestamp, trigger reason)

Some records may be processed twice after recovery, but state is consistent. This is the **exactly-once state** guarantee—state reflects each record exactly once through coordinated checkpoints.

---

### Problem 3: Scaling Without Downtime

**The problem:** We have 2 workers processing happily. Load increases. We need to add Worker C. But A and B have state in memory. We need to give C some partitions with their state. We can't stop processing for minutes while transferring state.

**The solution: The Hive Mind Model**

Workers are autonomous agents that join and leave freely. The server coordinates but doesn't control.

| Traditional Job Manager           | KurrentDB Hive Mind                    |
|-----------------------------------|----------------------------------------|
| Server starts workers             | Workers start themselves               |
| Server can pause/resume           | Workers decide when to leave           |
| Fixed parallelism                 | Dynamic parallelism                    |
| Manager deploys → Workers (owned) | Workers (autonomous) → Server (coord.) |

**Scaling workflow:**
1. Worker C connects
2. Server triggers checkpoint (capture current state)
3. All workers acknowledge checkpoint
4. Server reassigns partitions: A gets [0-42], B gets [43-85], C gets [86-127]
5. C downloads state for [86-127] from checkpoint
6. All workers resume
7. **Total downtime: seconds**

This is similar to Apache Flink's Reactive Mode (2021)—but it's our default architecture, not an opt-in mode.

---

### Problem 4: Coordinating Distributed Checkpoints

**The problem:** With multiple workers, how do we get a **consistent** checkpoint? If Worker A checkpoints at position 1000 and Worker B at position 2000, we don't have a coherent snapshot.

**The solution: Barrier Protocol (Chandy-Lamport Algorithm)**

Our checkpoint mechanism is based on the **Chandy-Lamport distributed snapshot algorithm** (1985)—the same foundation Apache Flink uses.

| Chandy-Lamport Concept | KurrentDB Implementation        |
|------------------------|---------------------------------|
| Marker message         | Barrier message                 |
| Process state          | Worker state (per partition)    |
| Channel state          | Not needed (single source log)  |
| Initiator              | Server (Checkpoint Coordinator) |

**The barrier flow:**
1. Server broadcasts Barrier to all workers
2. Each worker finishes current record
3. Each worker snapshots state and uploads to checkpoint storage
4. Each worker sends BarrierAck with positions
5. When all acks received → Checkpoint complete

**Key insight:** KurrentDB has a simpler topology than general distributed systems. There's a **single source log** feeding all workers—no complex fan-in of multiple channels. This means we don't need Flink's "barrier alignment" complexity. Each worker simply: receive barrier → finish record → snapshot → acknowledge.

---

### Problem 5: Managing Worker State

**The problem:** Workers need to store state locally (fast access), snapshot for checkpoints (serialize efficiently), restore on recovery (deserialize), and handle state for multiple partitions that may be added/removed during rebalancing.

**The solution: Pluggable State Store**

State management is the worker's responsibility, but we provide patterns and implementations:

```
Worker Process
├── IRecordProcessor (User Code)
├── State Store
│   ├── Partition 0 state
│   ├── Partition 1 state
│   └── Partition 2 state
└── Checkpoint Handler → uploads to Checkpoint Storage
```

**State store options:**
- **In-memory dictionary**: Simple, fast, limited size
- **DuckDB**: Queryable, persistent, snapshots to Parquet
- **RocksDB**: High performance, large state
- **Custom**: Whatever fits your use case

The `IStateStore` interface handles:
- `GetState<T>(partition)` — fast local access
- `SnapshotAsync()` — serialize for checkpoint
- `RestoreAsync()` — deserialize on recovery
- `AddPartitionsAsync()` / `RemovePartitionsAsync()` — rebalancing

---

### Problem 6: Handling Checkpoint Failures

**The problem:** What if a worker doesn't respond to a barrier? Options: wait forever (blocks progress), fail the whole processor (too aggressive), or make it configurable.

**The solution: Configurable Failure Policy**

```csharp
public enum CheckpointFailurePolicy {
    Continue,      // Disconnect failed workers, continue with remaining
    FailProcessor  // Disconnect all, processor goes idle
}
```

**Scenario: Worker Timeout**
1. Server sends Barrier to A, B, C
2. A and B acknowledge
3. C doesn't respond for 60 seconds
4. **Policy: Continue** → Disconnect C, mark checkpoint failed, reassign C's partitions, resume from last successful checkpoint
5. **Policy: FailProcessor** → Disconnect all, require manual intervention

No failure mode can cause inconsistent state. That's the Chandy-Lamport guarantee.

---

## The Killer Pattern: Client-Side Parallelism

A common concern: if one partition is slow, it blocks other partitions on the same worker's stream.

**The insight:** Nothing stops you from running multiple workers in the same process.

```
Your Process
├── Worker A (gRPC stream 1) → Partitions [0-42]
├── Worker B (gRPC stream 2) → Partitions [43-85]
└── Worker C (gRPC stream 3) → Partitions [86-127]

Server sees 3 workers. Doesn't know they're in the same process.
Checkpoint: sends Barrier to A, B, C. Waits for all 3 acks. Done.
```

**Why this works:** Each local worker has ONE input channel. The server coordinates barriers across workers—which it already does. The "alignment" happens server-side.

**What you get:**
- Parallel TCP connections (no head-of-line blocking)
- Parallel processing threads (CPU cores utilized)
- Independent backpressure (slow partition doesn't block fast ones)
- No code changes needed

**Two scaling dimensions:**
- **Horizontal**: Add more machines/pods/containers
- **Vertical**: Add more workers per machine

Example: 4 machines × 4 local workers = 16 workers → 128 partitions ÷ 16 = 8 partitions each

---

## The Architecture

### Server-Side Components

| Component                  | Responsibility                                                   |
|----------------------------|------------------------------------------------------------------|
| **Record Reader**          | Reads from $all, applies filters, routes to workers by partition |
| **Session Manager**        | Tracks worker connections, handles connect/disconnect lifecycle  |
| **Partition Assigner**     | Calculates partition assignments using consistent hashing        |
| **Checkpoint Coordinator** | Sends barriers, collects acks, manages checkpoint lifecycle      |
| **Checkpoint Storage**     | Stores/retrieves state, applies retention, manages savepoints    |
| **Processor Repository**   | Persists processor configuration (event-sourced)                 |

### Client-Side Components

| Component              | Responsibility                                                    |
|------------------------|-------------------------------------------------------------------|
| **ProcessorWorker**    | Main entry point; manages connection and orchestrates components  |
| **Gateway**            | Abstracts server communication (LegacyGateway now, GrpcGateway later) |
| **State Store**        | Local state storage (DuckDB, in-memory, etc.)                     |
| **Checkpoint Handler** | Snapshots state on barrier, uploads to server, sends ack          |
| **Position Tracker**   | Tracks processing progress per partition                          |
| **IRecordProcessor**   | User-implemented interface for processing records                 |

### The Gateway Abstraction

The Client SDK uses a **Ports & Adapters pattern**. Worker code talks to an `IGateway` interface, not directly to the server.

**Today:** `LegacyGateway` implements the full protocol client-side, using existing KurrentDB subscriptions. Ships without any server changes.

**Tomorrow:** `GrpcGateway` uses the real server protocol. Users swap gateways—**worker code requires zero changes.**

---

## What's Built vs. What's Next

### Phase 1: V2 Preview — DONE ✅

| Capability                 | Status       |
|----------------------------|--------------|
| Worker connection protocol | ✅ Complete  |
| Coordinated checkpointing (Chandy-Lamport) | ✅ Complete  |
| Record batching            | ✅ Complete  |
| Position tracking          | ✅ Complete  |
| Fluent builder API         | ✅ Complete  |
| Checkpoint triggers (interval, threshold, caught-up) | ✅ Complete |
| LegacyGateway (no server changes) | ✅ Complete |
| Pluggable state store interface | ✅ Complete |

**The single-worker version is done.** It works today, without any server changes.

### Phase 2: Multi-Worker — ~2-3 weeks

| Capability                 | Status       |
|----------------------------|--------------|
| Multiple workers per processor | 🔄 In Progress |
| Elastic scaling (workers join/leave) | 🔄 In Progress |
| Rebalance checkpoints      | 🔄 In Progress |
| Partition reassignment     | 🔄 In Progress |

**Multi-worker can work without server changes.** The LegacyGateway can simulate coordination. Server protocol is an optimization, not a blocker.

### Phase 3: Autonomous Mode — The Game Changer

| Capability                 | Status       |
|----------------------------|--------------|
| Distributed state store support | 🔧 Planned |
| Controller-less coordination | 🔧 Planned |
| True peer-to-peer worker mesh | 🔧 Planned |

**Autonomous Mode** eliminates the need for KurrentDB to act as a coordination controller. Workers still read records from KurrentDB—that's what we do, that's the product—but they coordinate through a distributed state store (Redis, DuckDB, etcd, etc.) instead of requiring server protocol support.

**Why is this possible?** The pluggable State Store abstraction. All coordination state—checkpoints, partition assignments, worker registry, settings—lives in the State Store. Workers become peers that discover each other and coordinate through shared state:

- **Records**: Always from KurrentDB (the $all log)
- **Coordination**: State Store handles checkpoints, assignments, worker discovery
- **Result**: True peer-to-peer mesh, no single point of coordination

**~2 weeks of work** once the distributed state store interface is defined. The architecture already supports this—it's implementation, not design work.

---

## Why Not Just Use Flink?

| Aspect                | Apache Flink                          | Kurrent Processors              |
|-----------------------|---------------------------------------|---------------------------------|
| **Deployment**        | Standalone cluster (JobManager + TaskManagers) | Embedded in KurrentDB |
| **Infrastructure**    | Separate cluster to maintain          | Zero additional components      |
| **Worker management** | Server deploys and controls workers   | Workers are autonomous          |
| **Data source**       | Multiple (Kafka, files, etc.)         | Single source ($all log)        |
| **Processing model**  | Operator DAGs                         | Simple worker model (no DAGs)   |
| **Barrier alignment** | Required for exactly-once             | Not needed (single source)      |
| **Learning curve**    | Significant (new paradigm)            | Familiar KurrentDB patterns     |

**What we borrowed from Flink:**
- Chandy-Lamport checkpoint barriers
- Exactly-once state semantics
- Savepoint concept
- Key groups / partitioning model

**What we simplified:**
- No barrier alignment (single source log)
- No operator DAGs (simple worker model)
- No watermarks or event-time (log is ordered)
- No separate cluster (embedded in KurrentDB)

**What we changed:**
- Workers are autonomous (Hive Mind), not controlled
- No deployment model—workers connect when ready
- Gateway abstraction for server-agnostic SDK

**This is "just enough" stream processing for KurrentDB users—not a Flink replacement.**

---

## Summary

| Problem                           | Solution                        |
|-----------------------------------|---------------------------------|
| Distributing stateful work        | Key groups / partitions         |
| Recovering from failures          | Coordinated checkpoints         |
| Scaling without downtime          | Hive Mind autonomous workers    |
| Coordinating distributed checkpoints | Chandy-Lamport barriers      |
| Managing worker state             | Pluggable state store           |
| Handling checkpoint failures      | Configurable failure policy     |
| Head-of-line blocking             | Client-side parallelism pattern |

**What we did:**
- Borrowed Chandy-Lamport checkpointing from Flink
- Simplified for KurrentDB's single-source topology
- Built autonomous worker model (Hive Mind)
- Shipped single-worker version without server changes

**The goal:** Make reliable stream processing on KurrentDB as easy as it should have been from the start.

---

*Sérgio Silveira — Developer Experience Team — January 2026*
