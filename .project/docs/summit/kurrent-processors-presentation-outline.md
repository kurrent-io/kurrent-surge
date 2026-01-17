# Kurrent Processors
## Summit Presentation Deck Outline V2

---

**Presenter:** Sérgio Silveira  
**Event:** Engineering Summit (London)  
**Duration:** ~35-45 minutes  
**Audience:** Internal engineers, product, dev advocates

### Version History

| Version | Date       | Changes |
|---------|------------|---------|
| 1.0     | 2026-01-16 | Initial draft |
| 2.0     | 2026-01-16 | Restructured around 6-problem narrative; added Client V2 slide |
| 2.1     | 2026-01-16 | Clarified Autonomous Mode: records from KurrentDB, coordination via State Store |

---

## Presentation Tool: Marp

**Install:** `npm install -g @marp-team/marp-cli`  
**Convert:** `marp deck.md --pptx` or `marp deck.md --pdf`  
**Preview:** `marp -p deck.md`

Marp uses Markdown with special directives. Supports Mermaid diagrams, speaker notes, themes.

---

## Narrative Structure: 6 Problems, 6 Solutions

This presentation follows the structure of the processors.md design doc—each problem leads to its solution. The audience learns WHY before WHAT.

---

## PART 1: THE PROBLEM (5 minutes)

### **SLIDE 1: Title**
```
KURRENT PROCESSORS
Six Problems. Six Solutions. One System.

Sérgio Silveira
Developer Experience Team
Engineering Summit 2026
```

Visual: Clean, dark background, Kurrent logo

---

### **SLIDE 2: The Crisis**
```
"We've lost customers to Kafka, Databricks, and ClickHouse
because processing on KurrentDB was too hard."
```

Visual: Quote on dark background. Let it land.

Speaker notes: Pause. This is the emotional hook. Name specific customers who left if you can.

---

### **SLIDE 3: The Developer Experience Today**
```
Building a processor today:

→ Manual position tracking        (error-prone)
→ Custom retry logic              (every team reinvents)
→ No coordination between workers (scaling is your problem)
→ Weeks of boilerplate            (before any business logic)
```

Visual: List with icons, red/warning colors

---

### **SLIDE 4: The Cascade**
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

Visual: Downward cascade/funnel diagram

---

### **SLIDE 5: The Gap**
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

            The GAP has become untenable.
```

Visual: Two boxes with arrows showing the gap

---

### **SLIDE 6: Built on Client V2**
```
PROCESSORS IS THE FLAGSHIP FEATURE OF CLIENT V2

New Language:               V2 Game Changers:
• Records (not events)      • SQL-based filter expressions
• Messages (payloads)       • Auto SerDe + Schema Registry
• Schema names              • Built-in resilience

FILTER EXPRESSIONS: Write SQL, filter records
┌──────────────────────────────────────────────────────────┐
│ stream LIKE 'order-%' AND                                │
│ properties.region IN ('us-east', 'us-west') AND          │
│ properties.amount > 100 AND                              │
│ timestamp >= '2024-01-01'                                │
└──────────────────────────────────────────────────────────┘
V1: stream name OR event type. That's it.
V2: Full SQL WHERE clause semantics. DuckDB compatible.
```

Visual: Code block with SQL-like filter, emphasize the contrast with V1

Speaker notes: "V2 gives you automatic serialization with Schema Registry—zero config. And filter expressions that look like SQL. These are platform capabilities. Processors builds on top of this foundation."

---

## PART 2: SIX PROBLEMS, SIX SOLUTIONS (20 minutes)

### **SLIDE 7: Overview**
```
KURRENT PROCESSORS

Six fundamental problems in distributed stream processing.
Six solutions that build on each other.
One complete system.
```

Visual: Number "6" prominently displayed

---

### **SLIDE 8: Problem 1 — Distributing Stateful Work**
```
PROBLEM 1

Stateful processing: records with the same key
must go to the same worker.

But we want parallelism.

If Worker A has state for Sensor-1 and Worker B joins...
→ How does B get some of A's state?
→ How do we split without losing data?
```

Visual: Question marks, thinking emoji

---

### **SLIDE 9: Solution 1 — Key Groups (Partitions)**
```
SOLUTION: PARTITIONS

              hash(key) % 128
Keys ────────────────────────────→ Partitions ───→ Workers

customer-123 ──→ Partition 47 ──┐
customer-456 ──→ Partition 47 ──┼──→ Worker A [0-63]
order-abc ─────→ Partition 99 ──┼──→ Worker B [64-127]
order-def ─────→ Partition 99 ──┘

• Fixed partition count (max_parallelism)
• Deterministic: same key → same partition
• Movable: partitions move, not keys
• State per partition
```

Visual: Flow diagram showing keys → partitions → workers

Speaker notes: "Adding Worker C? Move partitions 86-127 from B to C. State follows partition."

---

### **SLIDE 10: Problem 2 — Recovering from Failures**
```
PROBLEM 2

Worker A processing records 1000-2000.
Crashes at record 1500.

→ Did records 1000-1500 trigger side effects?
→ What state did Worker A have?
→ How do we resume without losing or duplicating work?
```

Visual: Crash icon, explosion emoji

---

### **SLIDE 11: Solution 2 — Coordinated Checkpoints**
```
SOLUTION: CHECKPOINTS

Log:  ═══════════════════════════════════════════▶
      1000   1250   1500   1750   2000
                     │              │
                     ▼              ▼
              Checkpoint      Checkpoint
                1500           2000

💥 Crash at 1750?
→ Restore checkpoint 1500
→ Replay 1500-1750
→ State is consistent
```

Visual: Timeline with checkpoints marked

Speaker notes: "Some records processed twice, but state reflects each record exactly once. That's the exactly-once state guarantee."

---

### **SLIDE 12: Problem 3 — Scaling Without Downtime**
```
PROBLEM 3

2 workers processing happily.
Load increases. Need Worker C.

But A and B have state in memory.
We need to give C partitions WITH their state.
We can't stop for minutes to transfer.
```

Visual: Scale/growth icon

---

### **SLIDE 13: Solution 3 — The Hive Mind Model**
```
SOLUTION: HIVE MIND

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

Workers are autonomous. They join when ready.
The processor EMERGES from coordination.
```

Visual: Side-by-side architecture comparison

Speaker notes: "This is Flink's Reactive Mode (2021), but as our default architecture."

---

### **SLIDE 14: Scaling Flow**
```
1. Worker C connects
2. Server triggers checkpoint
3. A, B acknowledge → state captured
4. Server reassigns:
   • A: [0-42]
   • B: [43-85]  
   • C: [86-127] + checkpoint
5. C downloads state for [86-127]
6. All resume

Total downtime: ~seconds
```

Visual: Sequence diagram or numbered steps

---

### **SLIDE 15: Problem 4 — Coordinating Distributed Checkpoints**
```
PROBLEM 4

Multiple workers. Need CONSISTENT checkpoint.

If Worker A checkpoints at position 1000
and Worker B checkpoints at position 2000...

We don't have a coherent snapshot.
```

Visual: Inconsistent state visualization

---

### **SLIDE 16: Solution 4 — Chandy-Lamport Barriers**
```
SOLUTION: BARRIER PROTOCOL

Based on Chandy-Lamport algorithm (1985)
Same foundation as Apache Flink.

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

Visual: Sequence diagram

Speaker notes: "This is the same algorithm Flink uses. Battle-tested since 1985."

---

### **SLIDE 17: The Key Insight**
```
WHY WE DON'T NEED BARRIER ALIGNMENT

Flink: Multiple input streams → Must wait for barriers
       from ALL sources before checkpointing (complex)

KurrentDB: ONE source ($all log) → No alignment needed
           Barrier arrives → checkpoint (simple)

Same algorithm. Simpler topology. Dramatically simpler.
```

Visual: Flink with multiple arrows vs. KurrentDB with single arrow

Speaker notes: "This is the key insight that makes our implementation simpler than Flink's."

---

### **SLIDE 18: Problem 5 — Managing Worker State**
```
PROBLEM 5

Workers need to:
• Store state locally (fast access)
• Snapshot for checkpoints (serialize)
• Restore on recovery (deserialize)
• Handle partitions added/removed (rebalancing)
```

Visual: State management complexity

---

### **SLIDE 19: Solution 5 — Pluggable State Store**
```
SOLUTION: IStateStore INTERFACE

Worker Process
├── Your Code (IRecordProcessor)
├── State Store
│   ├── Partition 0 state
│   ├── Partition 1 state
│   └── Partition 2 state
└── Checkpoint Handler → Storage

Options:
• In-memory dictionary (simple)
• DuckDB (queryable, Parquet snapshots)
• RocksDB (large state)
• Custom (whatever you need)
```

Visual: Component diagram

---

### **SLIDE 20: Problem 6 — Handling Checkpoint Failures**
```
PROBLEM 6

What if a worker doesn't respond to a barrier?

• Wait forever? (blocks all progress)
• Fail everything? (too aggressive)
• Something else?
```

Visual: Question/decision icon

---

### **SLIDE 21: Solution 6 — Configurable Failure Policy**
```
SOLUTION: YOU DECIDE

CheckpointFailurePolicy.Continue:
→ Disconnect failed worker
→ Mark checkpoint failed
→ Reassign partitions
→ Resume from last successful checkpoint

CheckpointFailurePolicy.FailProcessor:
→ Disconnect all workers
→ Processor goes idle
→ Manual intervention required

No failure mode can cause inconsistent state.
That's the Chandy-Lamport guarantee.
```

Visual: Two paths/options

---

### **SLIDE 22: BONUS — Client-Side Parallelism**
```
"What if one partition is slow?"

Run multiple workers IN THE SAME PROCESS.

Your Process
├── Worker A (gRPC stream 1) → [0-42]
├── Worker B (gRPC stream 2) → [43-85]
└── Worker C (gRPC stream 3) → [86-127]

Server sees 3 workers.
Doesn't know they're in one process!
Coordinates barriers across all. Done.

Two scaling dimensions:
• Horizontal: more machines
• Vertical: more workers per machine
```

Visual: Process containing multiple workers

Speaker notes: "This pattern emerges naturally from the Hive Mind architecture."

---

## PART 3: THE DEMO (5 minutes)

### **SLIDE 23: What We Built**
```
✅ Worker connection protocol
✅ Coordinated checkpointing (Chandy-Lamport)
✅ Record batching
✅ Position tracking
✅ Fluent builder API
✅ Multiple checkpoint triggers
✅ LegacyGateway (no server changes!)
✅ Pluggable state store

SINGLE-WORKER VERSION: DONE.
Works TODAY.
```

Visual: Green checkmarks, "DONE" emphasized

---

### **SLIDE 24: LIVE DEMO**
```
[LIVE DEMO]

• Simple processor starts
• Records flow
• Checkpoint happens automatically
• Kill the processor
• Restart
• Resumes from checkpoint position

"That's it. That's the feature."
```

Visual: Terminal/code window

Speaker notes: Keep it simple. The magic is that it works without ceremony.

---

## PART 4: THE FUTURE (5 minutes)

### **SLIDE 25: Roadmap**
```
Phase 1: Single Worker          ✅ DONE
├── All 6 problems solved
└── Works without server changes

Phase 2: Multi-Worker           ~2-3 weeks
├── Multiple workers per processor
├── Elastic scaling
└── Rebalance checkpoints

Phase 3: Autonomous Mode        ~2 weeks after
├── Distributed state store
├── Controller-less coordination
└── True peer-to-peer mesh       ← GAME CHANGER
```

Visual: Roadmap with checkmarks and progress indicators

---

### **SLIDE 26: Autonomous Mode — The "One More Thing"**
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

Records ALWAYS from KurrentDB — that's the product.
Coordination via State Store — no server protocol needed.
Workers form a mesh through shared state.

~2 weeks of work. The architecture already supports it.
```

Visual: Hub-and-spoke vs. mesh diagram, with KurrentDB as data source in both

Speaker notes: "Records always come from KurrentDB—that's what we do. But coordination? That can live in Redis, DuckDB, etcd. The State Store abstraction makes this possible. Workers discover each other, agree on partition assignments, store checkpoints—all through shared state. KurrentDB serves records. That's the separation."

---

### **SLIDE 27: Why Not Flink?**
```
BORROWED:               SIMPLIFIED:
• Chandy-Lamport        • No barrier alignment
• Exactly-once state    • No operator DAGs
• Savepoints            • No watermarks
• Key groups            • No separate cluster

CHANGED:
• Autonomous workers (Hive Mind)
• Gateway abstraction (server-agnostic SDK)
• Embedded in KurrentDB

"Just enough" stream processing for KurrentDB users.
Not a Flink replacement.
```

Visual: Two-column comparison

---

### **SLIDE 28: Closing**
```
KURRENT PROCESSORS

Six problems. Six solutions.
Done for single-worker. Multi-worker in weeks.

Make reliable stream processing
as easy as it should have been from the start.

Questions?
```

Visual: Clean closing slide

---

## BACKUP SLIDES (For Q&A)

### **BACKUP A: Chandy-Lamport Explained**
```
1985 algorithm for consistent distributed snapshots.

1. Coordinator sends marker (barrier) to all processes
2. Each process snapshots state on marker
3. Markers flow through system with data
4. Complete when all processes have processed marker

We use it. Flink uses it. Battle-tested.
```

### **BACKUP B: Barrier vs. Worker Positions**
```
Barrier position ≠ Worker position

Barrier says: "checkpoint at position 1000"
Worker A reports: "my last processed position: 997"
Worker B reports: "my last processed position: 1000"

Both are correct. Different partitions, different records.
Recovery uses barrier position (the consistent cut).
```

### **BACKUP C: Gateway Pattern**
```
Worker code → IGateway interface

Today:  LegacyGateway (simulates protocol client-side)
        Uses existing subscriptions. No server changes.

Later:  GrpcGateway (real server protocol)
        Better performance. Same worker code.

Swap gateways. Zero worker code changes.
```

### **BACKUP D: "Who asked for this?"**
```
Everyone who:
• Built custom retry logic
• Implemented manual checkpointing
• Asked "how do I scale subscriptions?"
• Migrated to Kafka because processing was "easier"
• Left us for Databricks or ClickHouse

The pain is real. We solved it.
```

---

## Timing Breakdown

| Section                          | Time    |
|----------------------------------|---------|
| Part 1: The Problem (slides 1-5) | 4 min   |
| Slide 6: Client V2 Foundation    | 2 min   |
| Part 2: 6 Problems (slides 7-22) | 18 min  |
| Part 3: Demo (slides 23-24)      | 5 min   |
| Part 4: Future (slides 25-28)    | 6 min   |
| Q&A                              | 10+ min |
| **Total**                        | ~45 min |

---

## Demo Script

**Setup:** Have a simple processor ready.

**Flow:**
1. Show the processor code (minimal—just `ProcessRecords`)
2. Start the processor
3. Show records being processed
4. Point out checkpoint being created (log message)
5. Kill the processor (Ctrl+C or kill process)
6. Restart it
7. Show it resuming from checkpoint position

**Key line:** "That's it. That's the feature. Position tracking, checkpointing, recovery—just works."

---

## Key Phrases to Use

- "Processors is the flagship feature of Client V2"
- "Filter expressions—SQL-like syntax V1 couldn't do"
- "The gap has become untenable"
- "Six problems, six solutions"
- "Same algorithm as Flink, simpler topology"
- "The processor emerges from coordination"
- "Single-worker is done. Works today."
- "Just enough stream processing"

---

*The narrative is: problem → solution, repeated 6 times. The audience learns WHY before WHAT. By the time you reach the demo, they understand the significance.*
