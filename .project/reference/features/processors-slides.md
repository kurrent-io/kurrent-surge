---
marp: true
theme: default
paginate: true
backgroundColor: #1a1a2e
color: #eaeaea
style: |
  section {
    font-family: 'Segoe UI', system-ui, sans-serif;
  }
  h1, h2 {
    color: #00d9ff;
  }
  h3 {
    color: #00ff9d;
  }
  code {
    background: #2d2d44;
  }
  table {
    font-size: 0.8em;
  }
  th {
    background: #00d9ff;
    color: #1a1a2e;
  }
  mermaid {
    background: transparent;
  }
---

# KurrentDB Processors

## Distributed Stream Processing for KurrentDB

**Version 2.6** | January 2026

---

# What Are Processors?

Distributed, fault-tolerant stream processing over the KurrentDB log.

**Key Capabilities:**

- **Elastic scaling** — Add/remove workers without stopping
- **Fault tolerance** — Automatic recovery, no data loss
- **Stateful processing** — Maintain state with automatic redistribution
- **Simple protocol** — Single gRPC bidirectional stream

---

# The Core Problem

> How do you parallelize **stateful** processing?

```
Stateless: Any worker can handle any record ✓ Easy

Stateful: Records with same key must go to same worker
         Worker A has running average for Sensor-1
         Worker B joins... now what?
```

**Challenges:**
- How does B get some of A's state?
- How do we split without losing data?
- How do we do this without stopping everything?

---

# The "Hive Mind" Model

Workers are **autonomous agents** that join and leave freely.

| Traditional Job Manager | KurrentDB Hive Mind |
|------------------------|---------------------|
| Server starts workers | Workers start themselves |
| Server can pause/resume | Workers decide when to leave |
| Fixed parallelism | Dynamic parallelism |

**The processor emerges from workers coordinating through the server.**

Similar to Apache Flink's Reactive Mode (2021).

---

# Hive Mind Architecture

```
                    ┌─────────────────────────────────────┐
                    │         KurrentDB Server            │
                    │  ┌─────────────────────────────┐    │
                    │  │    Processor Service        │    │
                    │  │  • Partition Assignment     │    │
                    │  │  • Checkpoint Coordination  │    │
                    │  │  • Record Routing           │    │
                    │  └─────────────────────────────┘    │
                    └──────────┬──────────┬───────────────┘
                               │          │
              ┌────────────────┘          └────────────────┐
              │                                            │
        ┌─────┴─────┐    ┌───────────┐    ┌───────────────┴┐
        │ Worker A  │    │ Worker B  │    │   Worker C     │
        │ [0-42]    │    │ [43-85]   │    │   [86-127]     │
        └───────────┘    └───────────┘    └────────────────┘
              │                │                   │
          Autonomous      Autonomous          Autonomous
```

---

# Solution: Partitions (Key Groups)

Don't assign keys to workers — use an intermediate layer.

```
Keys              →    hash(key) % 128    →    Workers
─────────────────────────────────────────────────────────
customer-123      →    Partition 47       →    Worker A [0-63]
customer-456      →    Partition 47       →    Worker A [0-63]
order-abc         →    Partition 99       →    Worker B [64-127]
order-def         →    Partition 99       →    Worker B [64-127]
```

**Key properties:**
- Fixed partition count (`max_parallelism`) — never changes
- Same key → same partition (deterministic)
- State follows partition, not individual keys

---

# Scaling: Add Worker C

```
BEFORE:  Worker A [0-63]     Worker B [64-127]

         ─────────── Worker C joins ───────────

AFTER:   Worker A [0-42]     Worker B [43-85]     Worker C [86-127]
```

**What happens:**
1. Checkpoint triggered (save current state)
2. Partitions reassigned
3. Worker C downloads state for [86-127]
4. Processing resumes

**Total downtime: ~seconds**

---

# Problem: Consistent Checkpoints

With multiple workers, how do we get a **consistent** snapshot?

```
Worker A checkpoints at position 1000
Worker B checkpoints at position 2000
                    ↓
         NOT a coherent snapshot!
```

**Solution: Chandy-Lamport Algorithm (1985)**

Server sends **barrier** (marker message) to all workers.
Workers snapshot state when they receive it.

---

# Barrier Protocol

```
Server                    Worker A              Worker B
   │                          │                     │
   │──── Barrier(ckpt-1) ────►│                     │
   │──── Barrier(ckpt-1) ─────────────────────────►│
   │                          │                     │
   │                    [finish record]       [finish record]
   │                    [snapshot state]      [snapshot state]
   │                    [upload to storage]   [upload to storage]
   │                          │                     │
   │◄─── BarrierAck ──────────│                     │
   │◄─── BarrierAck ────────────────────────────────│
   │                          │                     │
   │     Checkpoint COMPLETE  │                     │
```

---

# Barriers and Batching

What happens when checkpoint triggers mid-batch?

```
Server building batches:
  Worker A buffer: [pos 1, pos 3, pos 5, pos 7]  ← accumulating
  Worker B buffer: [pos 2, pos 4, pos 6]         ← accumulating

  Checkpoint triggers!

  1. FLUSH pending batches
  2. SEND barriers
  3. Resume normal batching
```

**Workers never see barriers mid-batch.** Simple protocol.

---

# Understanding Barrier IDs

**Barrier ID ≠ Position** — it's a coordination token.

```
Log positions:    1    2    3    4    5    6    7    8    9    10
Partition keys:   A    B    A    B    A    B    A    B    A    B

Worker 1 (A):     ✓    ·    ✓    ·    ✓    ·    ✓    ·    ✓    ·
Worker 2 (B):     ·    ✓    ·    ✓    ·    ✓    ·    ✓    ·    ✓
```

**Barrier sent at position 10:**
- Worker 1 reports: position **9** (last A record processed)
- Worker 2 reports: position **10** (last B record processed)

Each worker reports what **they** processed.

---

# Client-Side Parallelism

Nothing stops you from running multiple workers in the same process!

```
┌────────────────── Your Process ──────────────────┐
│                                                   │
│  ┌─────────┐   ┌─────────┐   ┌─────────┐        │
│  │Worker A │   │Worker B │   │Worker C │        │
│  │ [0-42]  │   │ [43-85] │   │[86-127] │        │
│  └────┬────┘   └────┬────┘   └────┬────┘        │
│       │             │             │              │
└───────┼─────────────┼─────────────┼──────────────┘
        │             │             │
        └─────────────┼─────────────┘
                      │
                      ▼
              ┌───────────────┐
              │    SERVER     │
              │ Sees 3 workers│
              └───────────────┘
```

Server doesn't know they're in the same process!

---

# Two Scaling Dimensions

```
Horizontal (machines):     4 machines
                            ×
Vertical (local workers):  4 workers per machine
                            =
Total:                     16 workers
                            ↓
Partitions:               128 ÷ 16 = 8 per worker
```

**Kubernetes:** `replicas=4`, `WORKER_COUNT=4` env var
**Auto-scaling:** Adjust either dimension based on lag

---

# Delivery Guarantees

| Scope | Guarantee | Responsibility |
|-------|-----------|----------------|
| Internal state (`IStateStore`) | **Exactly-once** | SDK handles automatically |
| External systems | **At-least-once** | User must handle idempotency |

**The boundary is clear:**
- State aggregations ✓ Safe
- Materialized views ✓ Safe
- State machines ✓ Safe
- Sending emails ⚠️ Need idempotency key
- External API calls ⚠️ Need idempotency
- Database writes ⚠️ Need upserts/version checks

---

# Safe Patterns

```csharp
// ✅ Safe: Aggregating in state
public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    foreach (var record in ctx.Records) {
        var reading = record.Deserialize<TemperatureReading>();
        var stats = ctx.GetState<SensorStats>(reading.SensorId);
        stats.Sum += reading.Value;
        stats.Count++;
    }
    return ValueTask.CompletedTask;
}
```

State is checkpointed. Recovery replays records but state reflects each exactly once.

---

# Idempotency for External Effects

```csharp
// External call needs idempotency key
public async ValueTask ProcessRecords(ProcessRecordsContext ctx) {
    foreach (var record in ctx.Records) {
        var order = record.Deserialize<OrderPlaced>();

        // Use record position as idempotency key
        var idempotencyKey = $"{ctx.ProcessorId}-{record.Position}";

        await _paymentService.ChargeAsync(
            order.CustomerId,
            order.Amount,
            idempotencyKey: idempotencyKey  // ← External system deduplicates
        );
    }
}
```

---

# The Client SDK

```csharp
public interface IRecordProcessor {
    // Only this is required
    ValueTask ProcessRecords(ProcessRecordsContext context);

    // Optional lifecycle hooks
    ValueTask OnInitialize(InitializationContext context);
    ValueTask OnCheckpoint(CheckpointContext context);
    ValueTask OnAssignmentChange(AssignmentChangeContext context);
    ValueTask OnShutdown(ShutdownContext context);
}
```

**Minimal interface.** Implement `ProcessRecords`, everything else is optional.

---

# Example: Customer Record Counter

```csharp
public class CustomerRecordCounter : IRecordProcessor {
    public ValueTask ProcessRecords(ProcessRecordsContext ctx) {
        foreach (var record in ctx.Records) {
            var partition = record.Properties["$partition"].GetInt32();
            var state = ctx.GetState<CustomerState>(partition);
            state.RecordCount++;
            state.LastRecordTime = record.Timestamp;
        }
        return ValueTask.CompletedTask;
    }
}

public class CustomerState {
    public long RecordCount { get; set; }
    public DateTimeOffset? LastRecordTime { get; set; }
}
```

---

# Running the Worker

```csharp
await using var worker = new ProcessorWorkerBuilder()
    .WithConnection("esdb://localhost:2113")
    .WithStateStore(new DuckDbStateStore("customer-state.db"))
    .Build();

await worker.RunAsync(
    processorId: "customer-counter",
    workerId: Environment.MachineName,
    processor: new CustomerRecordCounter(),
    settings: new ProcessorSettings {
        Subscription = new SubscriptionSettings {
            Filter = "stream_id LIKE 'customer-%'",
            Type = SubscriptionType.Keyed,
            PartitionKeySelector = "stream_id"
        }
    });
```

---

# Subscription Types

| Type | Behavior | Use Case |
|------|----------|----------|
| **EXCLUSIVE** | One active worker, others standby | Leader election |
| **SHARED** | Round-robin, no ordering | Max throughput |
| **KEYED** | Partition by key, per-key ordering | Stateful processing |

**KEYED** is the default for stateful processing.

---

# Checkpoint Configuration

```csharp
public record CheckpointSettings(
    TimeSpan Interval = 30s,      // Time between checkpoints
    int Threshold = 1000,         // Min records between checkpoints
    TimeSpan Timeout = 60s,       // Max wait for worker response
    int RetentionCount = 3,       // Checkpoints to keep
    CheckpointFailurePolicy FailurePolicy = Continue
);
```

**Failure policies:**
- `Continue` — Disconnect failed worker, continue with others
- `FailProcessor` — Stop everything, require manual intervention

---

# Observability (OTEL Metrics)

**Server-side:**
| Metric | Type | Description |
|--------|------|-------------|
| `processor.records.processed` | Counter | Total records |
| `processor.checkpoint.duration` | Histogram | Checkpoint time |
| `processor.workers.count` | Gauge | Connected workers |

**Client-side:**
| Metric | Type | Description |
|--------|------|-------------|
| `worker.lag.records` | Gauge | Records behind |
| `worker.lag.time` | Gauge | Time behind |
| `worker.partitions.count` | Gauge | Assigned partitions |

---

# Architecture Overview

```
┌─────────────────── SERVER ───────────────────────┐
│  ┌──────────────────────────────────────────┐    │
│  │           Processor Service               │    │
│  │  ┌─────────┐ ┌─────────┐ ┌────────────┐  │    │
│  │  │ Record  │ │Partition│ │ Checkpoint │  │    │
│  │  │ Reader  │ │Assigner │ │Coordinator │  │    │
│  │  └─────────┘ └─────────┘ └────────────┘  │    │
│  └──────────────────────────────────────────┘    │
│                       ↑                          │
│                    $all Log                      │
└──────────────────────┬───────────────────────────┘
                       │ gRPC (bidirectional)
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
   ┌─────────┐   ┌─────────┐   ┌─────────┐
   │Worker A │   │Worker B │   │Worker C │
   │StateStore│   │StateStore│  │StateStore│
   └─────────┘   └─────────┘   └─────────┘
```

---

# Implementation Roadmap

### Phase 1: Core Protocol (MVP)
- gRPC service, session management
- Partition assignment, record routing
- Basic checkpoint coordination
- Single subscription type (KEYED)

### Phase 2: Production Ready
- All subscription types
- DuckDB checkpoint storage
- Failure policies, savepoints
- Management APIs

---

# Implementation Roadmap (cont.)

### Phase 3: Client SDK
- .NET ProcessorWorker SDK
- DuckDB state store
- Reconnection with backoff
- High-level `IRecordProcessor` API

### Phase 4: Observability
- Lag metrics with index support
- Dashboard templates
- Alerting recommendations

---

# Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| No server-hosted workers | Simpler server, user flexibility |
| No barrier alignment | Single input channel per worker |
| No incremental checkpoints (v1) | MVP simplicity, add later |
| No exactly-once sinks | Worker responsibility |
| Chandy-Lamport barriers | Proven algorithm (1985) |
| Fixed partition count | Deterministic, minimal rebalance |

---

# What We're NOT Building (v1)

- **Operator DAGs** (filter→map→aggregate chains)
- **Server-hosted workers** (we coordinate, not execute)
- **Exactly-once to external systems** (user's responsibility)
- **Incremental checkpoints** (full state snapshots for now)

These can be added later if needed.

---

# Recovery Scenario

```
Worker A processing records 1000-2000
                    ↓
              Crash at 1750!
                    ↓
         Last checkpoint: 1500
                    ↓
    1. Restore state from checkpoint 1500
    2. Replay records 1500-1750
    3. Continue processing
                    ↓
         Some records processed twice
         BUT state is consistent ✓
```

---

# Summary

**KurrentDB Processors provide:**

✓ Elastic scaling without downtime
✓ Fault tolerance with automatic recovery
✓ Stateful processing with checkpointing
✓ Simple autonomous worker model
✓ Clear delivery guarantees
✓ Minimal SDK interface

**Built on proven foundations:**
- Chandy-Lamport distributed snapshots (1985)
- Partition-based scaling (Kafka, Flink)
- Autonomous worker model (Flink Reactive Mode)

---

# Questions?

**Resources:**
- Design doc: `processors.md`
- Review notes: `processors-review-notes.md`
- Proto: `processors_service_v7.proto`

**References:**
- Chandy-Lamport paper (1985)
- Apache Flink Checkpointing docs
- Flink Reactive Mode announcement (2021)

---

# Next Steps

1. **Review** design document and review notes
2. **Discuss** open questions from architecture review
3. **Phase 1** implementation kick-off
4. **Prototype** basic worker↔server communication

**Target:** MVP with KEYED subscriptions and basic checkpointing

