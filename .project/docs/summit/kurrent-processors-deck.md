---
marp: true
theme: uncover
class: invert
paginate: true
backgroundColor: #1a1a2e
color: #eee
style: |
  section {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
  }
  code {
    background: #16213e;
    border-radius: 4px;
  }
  pre {
    background: #16213e;
    border-radius: 8px;
    padding: 1em;
  }
  h1 {
    color: #00d4ff;
  }
  h2 {
    color: #00d4ff;
  }
  strong {
    color: #ff6b6b;
  }
  em {
    color: #ffd93d;
  }
  .small {
    font-size: 0.7em;
  }
---

<!-- _class: lead invert -->

# KURRENT PROCESSORS

## Six Problems. Six Solutions. One System.

**Sérgio Silveira**
Developer Experience Team
Engineering Summit 2026

---

<!-- _class: lead invert -->

# "We've lost customers to Kafka, Databricks, and ClickHouse because processing on KurrentDB was too hard."

<!-- Pause. This is the emotional hook. Name specific customers who left if you can. -->

---

# The Developer Experience Today

Building a processor today:

→ Manual position tracking *(error-prone)*
→ Custom retry logic *(every team reinvents)*
→ No coordination between workers *(scaling is your problem)*
→ Weeks of boilerplate *(before any business logic)*

---

# The Cascade

```
Incorrect checkpointing
         ↓
Lost events + Duplicates
         ↓
Support burden (same questions, repeatedly)
         ↓
Developer abandonment
         ↓
Customers migrate to competitors
```

---

# The Gap

```
┌─────────────────────────┐      ┌─────────────────────────┐
│   What KurrentDB        │      │    What users           │
│   provides              │      │    need                 │
├─────────────────────────┤      ├─────────────────────────┤
│ Low-level subscriptions │  →   │ Reliable processing     │
│ Manual checkpointing    │  →   │ Automatic checkpoints   │
│ Single-worker only      │  →   │ Horizontal scaling      │
│ No coordination         │  →   │ Fault tolerance         │
└─────────────────────────┘      └─────────────────────────┘
```

**The GAP has become untenable.**

---

# Built on Client V2

**PROCESSORS IS THE FLAGSHIP FEATURE OF CLIENT V2**

| New Language | V2 Game Changers |
|--------------|------------------|
| Records (not events) | SQL-based filter expressions |
| Messages (payloads) | Auto SerDe + Schema Registry |
| Schema names | Built-in resilience |

---

# Filter Expressions

Write SQL, filter records:

```sql
stream LIKE 'order-%' AND
properties.region IN ('us-east', 'us-west') AND
properties.amount > 100 AND
timestamp >= '2024-01-01'
```

**V1:** stream name OR event type. That's it.
**V2:** Full SQL WHERE clause semantics. DuckDB compatible.

<!-- V2 gives you automatic serialization with Schema Registry—zero config. And filter expressions that look like SQL. These are platform capabilities. Processors builds on top of this foundation. -->

---

<!-- _class: lead invert -->

# KURRENT PROCESSORS

Six fundamental problems in distributed stream processing.
Six solutions that build on each other.
**One complete system.**

---

# Problem 1 — Distributing Stateful Work

Stateful processing: records with the same key
**must go to the same worker.**

But we want parallelism.

If Worker A has state for Sensor-1 and Worker B joins...
→ How does B get some of A's state?
→ How do we split without losing data?

---

# Solution 1 — Key Groups (Partitions)

```
              hash(key) % 128
Keys ────────────────────────────→ Partitions ───→ Workers

customer-123 ──→ Partition 47 ──┐
customer-456 ──→ Partition 47 ──┼──→ Worker A [0-63]
order-abc ─────→ Partition 99 ──┼──→ Worker B [64-127]
order-def ─────→ Partition 99 ──┘
```

- **Fixed** partition count (max_parallelism)
- **Deterministic:** same key → same partition
- **Movable:** partitions move, not keys

<!-- Adding Worker C? Move partitions 86-127 from B to C. State follows partition. -->

---

# Problem 2 — Recovering from Failures

Worker A processing records 1000-2000.
**Crashes at record 1500.**

→ Did records 1000-1500 trigger side effects?
→ What state did Worker A have?
→ How do we resume without losing or duplicating work?

---

# Solution 2 — Coordinated Checkpoints

```
Log:  ═══════════════════════════════════════════▶
      1000   1250   1500   1750   2000
                     │              │
                     ▼              ▼
              Checkpoint      Checkpoint
                1500           2000
```

💥 **Crash at 1750?**
→ Restore checkpoint 1500
→ Replay 1500-1750
→ State is consistent

<!-- Some records processed twice, but state reflects each record exactly once. That's the exactly-once state guarantee. -->

---

# Problem 3 — Scaling Without Downtime

2 workers processing happily.
Load increases. **Need Worker C.**

But A and B have state in memory.
We need to give C partitions *WITH* their state.
**We can't stop for minutes to transfer.**

---

# Solution 3 — The Hive Mind Model

```
Traditional:                 Kurrent Processors:
┌───────────────┐           ┌───────────────────────┐
│  Job Manager  │           │      KurrentDB        │
│  (controls)   │           │   (coordinates)       │
└───────┬───────┘           └───────────┬───────────┘
        │                               │
┌───────┴───────┐           ┌───────────┴───────────┐
│    Workers    │           │  Autonomous Workers   │
│  (assigned)   │           │  (connect/disconnect) │
└───────────────┘           └───────────────────────┘
```

Workers are autonomous. They join when ready.
**The processor EMERGES from coordination.**

<!-- This is Flink's Reactive Mode (2021), but as our default architecture. -->

---

# Scaling Flow

1. Worker C connects
2. Server triggers checkpoint
3. A, B acknowledge → state captured
4. Server reassigns:
   - A: [0-42]
   - B: [43-85]
   - C: [86-127] + checkpoint
5. C downloads state for [86-127]
6. All resume

**Total downtime: ~seconds**

---

# Problem 4 — Coordinating Distributed Checkpoints

Multiple workers. Need **CONSISTENT** checkpoint.

If Worker A checkpoints at position 1000
and Worker B checkpoints at position 2000...

**We don't have a coherent snapshot.**

---

# Solution 4 — Chandy-Lamport Barriers

Based on Chandy-Lamport algorithm (1985)
*Same foundation as Apache Flink.*

```
Server                Workers A & B
  │                        │
  │──── Barrier(1000) ────▶│
  │                        │ Finish current record
  │                        │ Snapshot state
  │                        │ Upload to storage
  │◀─── BarrierAck ────────│
  │                        │
  ✓ Checkpoint 1000 complete
```

<!-- This is the same algorithm Flink uses. Battle-tested since 1985. -->

---

# The Key Insight

## WHY WE DON'T NEED BARRIER ALIGNMENT

**Flink:** Multiple input streams → Must wait for barriers
from ALL sources before checkpointing *(complex)*

**KurrentDB:** ONE source ($all log) → No alignment needed
Barrier arrives → checkpoint *(simple)*

**Same algorithm. Simpler topology. Dramatically simpler.**

<!-- This is the key insight that makes our implementation simpler than Flink's. -->

---

# Problem 5 — Managing Worker State

Workers need to:
- Store state locally *(fast access)*
- Snapshot for checkpoints *(serialize)*
- Restore on recovery *(deserialize)*
- Handle partitions added/removed *(rebalancing)*

---

# Solution 5 — Pluggable State Store

```
Worker Process
├── Your Code (IRecordProcessor)
├── State Store
│   ├── Partition 0 state
│   ├── Partition 1 state
│   └── Partition 2 state
└── Checkpoint Handler → Storage
```

**Options:**
- In-memory dictionary *(simple)*
- DuckDB *(queryable, Parquet snapshots)*
- RocksDB *(large state)*
- Custom *(whatever you need)*

---

# Problem 6 — Handling Checkpoint Failures

What if a worker doesn't respond to a barrier?

- Wait forever? *(blocks all progress)*
- Fail everything? *(too aggressive)*
- Something else?

---

# Solution 6 — Configurable Failure Policy

**CheckpointFailurePolicy.Continue:**
→ Disconnect failed worker
→ Mark checkpoint failed
→ Reassign partitions
→ Resume from last successful checkpoint

**CheckpointFailurePolicy.FailProcessor:**
→ Disconnect all workers
→ Processor goes idle
→ Manual intervention required

*No failure mode can cause inconsistent state.*
**That's the Chandy-Lamport guarantee.**

---

# BONUS — Client-Side Parallelism

*"What if one partition is slow?"*

Run multiple workers **IN THE SAME PROCESS**.

```
Your Process
├── Worker A (gRPC stream 1) → [0-42]
├── Worker B (gRPC stream 2) → [43-85]
└── Worker C (gRPC stream 3) → [86-127]
```

Server sees 3 workers. Doesn't know they're in one process!

**Two scaling dimensions:**
- Horizontal: more machines
- Vertical: more workers per machine

<!-- This pattern emerges naturally from the Hive Mind architecture. -->

---

# What We Built

✅ Worker connection protocol
✅ Coordinated checkpointing (Chandy-Lamport)
✅ Record batching
✅ Position tracking
✅ Fluent builder API
✅ Multiple checkpoint triggers
✅ LegacyGateway (no server changes!)
✅ Pluggable state store

## SINGLE-WORKER VERSION: DONE.
**Works TODAY.**

---

<!-- _class: lead invert -->

# LIVE DEMO

- Simple processor starts
- Records flow
- Checkpoint happens automatically
- Kill the processor
- Restart
- Resumes from checkpoint position

**"That's it. That's the feature."**

<!-- Keep it simple. The magic is that it works without ceremony. -->

---

# Roadmap

**Phase 1: Single Worker** ✅ DONE
├── All 6 problems solved
└── Works without server changes

**Phase 2: Multi-Worker**
├── Multiple workers per processor
├── Elastic scaling
└── Rebalance checkpoints

**Phase 3: Autonomous Mode**
├── Distributed state store
├── Controller-less coordination
└── True peer-to-peer mesh ← *GAME CHANGER*

---

# Autonomous Mode — The "One More Thing"

```
TODAY (with server protocol):
Workers ←→ KurrentDB Server ←→ Workers
            (coordinates)

AUTONOMOUS MODE:
Workers ←→ State Store ←→ Workers
         (peer-to-peer)
              ↓
         KurrentDB
       (records only)
```

Records **ALWAYS** from KurrentDB — that's the product.
Coordination via State Store — no server protocol needed.

<!-- Records always come from KurrentDB—that's what we do. But coordination? That can live in Redis, DuckDB, etcd. The State Store abstraction makes this possible. -->

---

# Why Not Flink?

| BORROWED | SIMPLIFIED |
|----------|------------|
| Chandy-Lamport | No barrier alignment |
| Exactly-once state | No operator DAGs |
| Savepoints | No watermarks |
| Key groups | No separate cluster |

**CHANGED:**
- Autonomous workers (Hive Mind)
- Gateway abstraction (server-agnostic SDK)
- Embedded in KurrentDB

*"Just enough" stream processing for KurrentDB users.*

---

<!-- _class: lead invert -->

# KURRENT PROCESSORS

Six problems. Six solutions.
Done for single-worker. Multi-worker in weeks.

**Make reliable stream processing
as easy as it should have been from the start.**

## Questions?

---

<!-- _class: lead invert -->

# BACKUP SLIDES

---

# Backup A: Chandy-Lamport Explained

1985 algorithm for consistent distributed snapshots.

1. Coordinator sends marker (barrier) to all processes
2. Each process snapshots state on marker
3. Markers flow through system with data
4. Complete when all processes have processed marker

**We use it. Flink uses it. Battle-tested.**

---

# Backup B: Barrier vs. Worker Positions

**Barrier position ≠ Worker position**

Barrier says: "checkpoint at position 1000"
Worker A reports: "my last processed position: 997"
Worker B reports: "my last processed position: 1000"

Both are correct. Different partitions, different records.
**Recovery uses barrier position (the consistent cut).**

---

# Backup C: Gateway Pattern

```
Worker code → IGateway interface
```

**Today:** LegacyGateway (simulates protocol client-side)
Uses existing subscriptions. No server changes.

**Later:** GrpcGateway (real server protocol)
Better performance. Same worker code.

*Swap gateways. Zero worker code changes.*

---

# Backup D: "Who asked for this?"

Everyone who:
- Built custom retry logic
- Implemented manual checkpointing
- Asked "how do I scale subscriptions?"
- Migrated to Kafka because processing was "easier"
- Left us for Databricks or ClickHouse

**The pain is real. We solved it.**
