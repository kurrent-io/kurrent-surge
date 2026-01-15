# KurrentDB Processors vs Apache Flink

## Comparison Guide

**Version:** 1.0
**Date:** December 2025
**Audience:** Engineers familiar with Flink, technical evaluators, architecture reviewers

---

## Executive Summary

KurrentDB Processors is a distributed stream processing system built into KurrentDB. While inspired by Apache Flink's checkpointing mechanisms, it takes a fundamentally different architectural approach.

**Key differences at a glance:**

| Aspect                | Apache Flink                                   | KurrentDB Processors                |
|-----------------------|------------------------------------------------|-------------------------------------|
| **Deployment**        | Standalone cluster (JobManager + TaskManagers) | Embedded in KurrentDB               |
| **Worker management** | Server deploys and controls workers            | Workers are autonomous (Hive Mind)  |
| **Data source**       | Multiple (Kafka, files, sockets, etc.)         | Single source ($all log)            |
| **Processing model**  | Operator DAGs                                  | Simple worker model (no DAGs)       |
| **Barrier alignment** | Required for exactly-once                      | Not needed (single source)          |
| **State backend**     | RocksDB, heap, etc.                            | Pluggable (DuckDB, in-memory, etc.) |

---

## Table of Contents

1. [Architecture Comparison](#1-architecture-comparison)
2. [Terminology Mapping](#2-terminology-mapping)
3. [Checkpointing: What We Borrowed](#3-checkpointing-what-we-borrowed)
4. [What We Simplified](#4-what-we-simplified)
5. [What We Changed](#5-what-we-changed)
6. [Feature Comparison Matrix](#6-feature-comparison-matrix)
7. [Scaling Comparison](#7-scaling-comparison)
8. [Failure Handling Comparison](#8-failure-handling-comparison)
9. [When to Use Which](#9-when-to-use-which)

---

## 1. Architecture Comparison

### Apache Flink Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         APACHE FLINK                                     │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                        JobManager                                │   │
│   │   • Accepts job submissions                                      │   │
│   │   • Schedules tasks to TaskManagers                             │   │
│   │   • Coordinates checkpoints                                      │   │
│   │   • Handles failover                                            │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                              │                                           │
│              ┌───────────────┼───────────────┐                          │
│              │               │               │                          │
│              ▼               ▼               ▼                          │
│   ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐          │
│   │  TaskManager 1  │ │  TaskManager 2  │ │  TaskManager 3  │          │
│   │  ┌───────────┐  │ │  ┌───────────┐  │ │  ┌───────────┐  │          │
│   │  │  Slot 1   │  │ │  │  Slot 1   │  │ │  │  Slot 1   │  │          │
│   │  │  Slot 2   │  │ │  │  Slot 2   │  │ │  │  Slot 2   │  │          │
│   │  └───────────┘  │ │  └───────────┘  │ │  └───────────┘  │          │
│   └─────────────────┘ └─────────────────┘ └─────────────────┘          │
│                                                                          │
│   Data Sources:                                                         │
│   ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐                          │
│   │ Kafka  │ │  File  │ │ Socket │ │ Custom │                          │
│   └────────┘ └────────┘ └────────┘ └────────┘                          │
│                                                                          │
│   Processing: Operator DAGs                                             │
│   Source ──▶ Map ──▶ KeyBy ──▶ Window ──▶ Aggregate ──▶ Sink           │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**Flink characteristics:**
- Standalone distributed system
- JobManager is the central controller
- TaskManagers are deployed and managed by JobManager
- Jobs are compiled into operator DAGs
- Multiple data sources supported
- Complex topology with fan-in/fan-out

### KurrentDB Processors Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      KURRENTDB PROCESSORS                                │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                    KurrentDB Server                              │   │
│   │   ┌─────────────────────────────────────────────────────────┐   │   │
│   │   │                  Processor Service                       │   │   │
│   │   │   • Accepts worker connections                          │   │   │
│   │   │   • Assigns partitions                                  │   │   │
│   │   │   • Coordinates checkpoints                             │   │   │
│   │   │   • Routes records to workers                           │   │   │
│   │   └─────────────────────────────────────────────────────────┘   │   │
│   │                                                                  │   │
│   │   ┌─────────────────────────────────────────────────────────┐   │   │
│   │   │                     $all Log                             │   │   │
│   │   │   (Single source of truth)                              │   │   │
│   │   └─────────────────────────────────────────────────────────┘   │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                              │                                           │
│              ┌───────────────┼───────────────┐                          │
│              │               │               │                          │
│              ▼               ▼               ▼                          │
│   ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐          │
│   │    Worker A     │ │    Worker B     │ │    Worker C     │          │
│   │   (autonomous)  │ │   (autonomous)  │ │   (autonomous)  │          │
│   │                 │ │                 │ │                 │          │
│   │  Partitions     │ │  Partitions     │ │  Partitions     │          │
│   │    [0-42]       │ │   [43-85]       │ │   [86-127]      │          │
│   └─────────────────┘ └─────────────────┘ └─────────────────┘          │
│                                                                          │
│   Data Source: $all log only                                            │
│                                                                          │
│   Processing: Simple record processing (no DAGs)                        │
│   Record ──▶ IRecordProcessor ──▶ State Update                          │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**KurrentDB Processors characteristics:**
- Embedded in KurrentDB (no separate cluster)
- Server coordinates but doesn't control workers
- Workers are autonomous (connect/disconnect freely)
- Single data source ($all log)
- Simple topology (fan-out only, no fan-in)
- No operator DAGs

### The "Hive Mind" vs "Job Manager" Model

| Aspect              | Flink JobManager                            | KurrentDB Hive Mind                               |
|---------------------|---------------------------------------------|---------------------------------------------------|
| Worker lifecycle    | Server starts/stops workers                 | Workers start/stop themselves                     |
| Parallelism changes | Requires job restart (except Reactive Mode) | Workers join/leave dynamically                    |
| Failure recovery    | JobManager restarts failed tasks            | Server reassigns partitions to remaining workers  |
| Deployment          | Server needs container orchestration        | Workers deploy however you want                   |
| Scaling decision    | Made by JobManager or external autoscaler   | Made by whatever runs workers (K8s, manual, etc.) |

**Flink Reactive Mode (since 1.13):** Flink added Reactive Mode in May 2021, which allows dynamic scaling similar to our Hive Mind model. However, it's an opt-in mode, not the default architecture.

---

## 2. Terminology Mapping

| Flink Term         | KurrentDB Term     | Notes                                                           |
|--------------------|--------------------|-----------------------------------------------------------------|
| Job                | Processor          | A logical processing unit                                       |
| Task               | Worker             | A process that executes processing                              |
| TaskManager        | Worker             | Flink TaskManagers host multiple slots; our workers are simpler |
| Slot               | (no equivalent)    | We don't subdivide workers                                      |
| JobManager         | Processor Service  | Coordination component                                          |
| Operator           | (no equivalent)    | We don't have operator DAGs                                     |
| Key Group          | Partition          | Subdivision of key space                                        |
| Parallelism        | max_parallelism    | Number of partitions                                            |
| Checkpoint         | Checkpoint         | Consistent snapshot                                             |
| Savepoint          | Savepoint          | Manual checkpoint (never auto-deleted)                          |
| Checkpoint Barrier | Barrier            | Marker triggering checkpoint                                    |
| State Backend      | State Store        | Where state is stored                                           |
| Checkpoint Storage | Checkpoint Storage | Where checkpoints are persisted                                 |
| Source             | Record Reader      | Reads input data                                                |
| Sink               | (user code)        | Writes output (worker's responsibility)                         |
| Watermark          | (not implemented)  | Event time tracking                                             |
| Event Time         | (not implemented)  | We use processing time                                          |
| Keyed State        | Partitioned State  | State scoped to a key/partition                                 |
| Operator State     | (no equivalent)    | We only have partitioned state                                  |

---

## 3. Checkpointing: What We Borrowed

Our checkpoint mechanism is directly inspired by Flink, which itself is based on the Chandy-Lamport distributed snapshot algorithm (1985).

### The Chandy-Lamport Foundation

Both Flink and KurrentDB Processors use the same fundamental algorithm:

1. **Initiator sends markers**: Coordinator broadcasts a marker (barrier) to all processes
2. **Processes snapshot on marker**: Each process saves its state when it receives the marker
3. **Marker propagation**: Markers flow through the system with data
4. **Snapshot completion**: When all processes have processed the marker, snapshot is complete

### What We Took From Flink

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SHARED CONCEPTS                                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ✅ Barrier-based checkpointing                                        │
│      • Coordinator sends barrier to all workers                         │
│      • Workers snapshot state on barrier                                │
│      • Workers acknowledge completion                                   │
│      • Coordinator marks checkpoint complete when all ack               │
│                                                                          │
│   ✅ Checkpoint ID as position                                          │
│      • Checkpoint ID represents a position in the log                   │
│      • Recovery restarts from checkpoint position                       │
│                                                                          │
│   ✅ Savepoints                                                         │
│      • Manual checkpoints that are never auto-deleted                   │
│      • Used for upgrades, migrations, debugging                         │
│                                                                          │
│   ✅ Checkpoint retention                                               │
│      • Keep N most recent checkpoints                                   │
│      • Delete older ones automatically                                  │
│                                                                          │
│   ✅ Configurable checkpoint interval                                   │
│      • Time-based triggering                                            │
│      • Balance between overhead and recovery time                       │
│                                                                          │
│   ✅ Exactly-once state semantics                                       │
│      • State reflects each record exactly once                          │
│      • Achieved through checkpoint + replay                             │
│                                                                          │
│   ✅ At-least-once delivery                                             │
│      • Records may be replayed after recovery                           │
│      • Worker handles deduplication for external systems                │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Barrier Protocol Comparison

**Flink:**
```
Checkpoint Coordinator ──▶ Barrier ──▶ Source Operators
                                            │
                                            ▼
                          ┌─────────────────────────────────┐
                          │   Barrier flows through DAG     │
                          │   with data records             │
                          └─────────────────────────────────┘
                                            │
                                            ▼
                          All operators snapshot when barrier arrives
                                            │
                                            ▼
                          Checkpoint Coordinator receives all acks
```

**KurrentDB:**
```
Checkpoint Coordinator ──▶ Barrier ──▶ All Workers (parallel)
                                            │
                                            ▼
                          Workers snapshot immediately
                          (no DAG traversal needed)
                                            │
                                            ▼
                          Checkpoint Coordinator receives all acks
```

---

## 4. What We Simplified

### 4.1 No Barrier Alignment

**Flink's challenge:**

When an operator has multiple input channels (fan-in), barriers arrive at different times:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    FLINK: BARRIER ALIGNMENT NEEDED                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   Source A ────[data]────[barrier]────[data]────▶ ┐                     │
│                                                    ├──▶ Join Operator   │
│   Source B ────[data]────[data]────[barrier]────▶ ┘                     │
│                                                                          │
│   Problem:                                                              │
│   • Barrier from A arrives first                                        │
│   • But B still has pre-barrier data in flight                         │
│   • Must BUFFER A's post-barrier data until B's barrier arrives        │
│                                                                          │
│   This is "barrier alignment" - complex and adds latency                │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**KurrentDB's simplification:**

We have a single source ($all log) and no fan-in at workers:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    KURRENTDB: NO ALIGNMENT NEEDED                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│                    $all log (single source)                             │
│                           │                                              │
│                           ▼                                              │
│                    ┌─────────────┐                                      │
│                    │   Server    │                                      │
│                    │  (fan-out)  │                                      │
│                    └──────┬──────┘                                      │
│                           │                                              │
│           ┌───────────────┼───────────────┐                             │
│           │               │               │                             │
│           ▼               ▼               ▼                             │
│      Worker A        Worker B        Worker C                           │
│    (one input)     (one input)     (one input)                         │
│                                                                          │
│   Each worker has exactly ONE input channel                             │
│   Barrier arrives once, snapshot immediately                            │
│   No buffering, no alignment, no complexity                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**Impact:**
- Simpler implementation
- Lower checkpoint latency
- No backpressure from alignment buffering
- Easier to reason about

### 4.2 No Operator DAGs

**Flink:**
```java
DataStream<Event> stream = env.addSource(kafkaSource);

stream
    .filter(e -> e.getType().equals("ORDER"))
    .keyBy(e -> e.getCustomerId())
    .window(TumblingEventTimeWindows.of(Time.minutes(5)))
    .aggregate(new OrderAggregator())
    .addSink(new OrderSink());
```

This compiles to a DAG of operators, each potentially on different TaskManagers.

**KurrentDB:**
```csharp
public class OrderProcessor : IRecordProcessor
{
    public ValueTask ProcessRecords(ProcessRecordsContext ctx)
    {
        foreach (var record in ctx.Records)
        {
            // All processing in one place
            if (record.MessageType == "ORDER")
            {
                var state = ctx.GetState<OrderState>(record.Partition);
                state.Aggregate(record);
            }
        }
        return ValueTask.CompletedTask;
    }
}
```

All processing happens in the worker. No distributed operators, no shuffle, no network between stages.

**Trade-offs:**

| Aspect      | Flink (DAGs)                   | KurrentDB (No DAGs)                          |
|-------------|--------------------------------|----------------------------------------------|
| Flexibility | High - complex topologies      | Lower - single-stage processing              |
| Performance | Network shuffle between stages | No shuffle overhead                          |
| Complexity  | High - many moving parts       | Low - simple model                           |
| Debugging   | Hard - distributed operators   | Easy - all in one process                    |
| Use cases   | Complex ETL, ML pipelines      | Record processing, projections, aggregations |

### 4.3 No Watermarks / Event Time

**Flink** has sophisticated event-time processing with watermarks:
- Track event time progress
- Handle out-of-order events
- Trigger windows based on event time

**KurrentDB Processors** uses processing time only:
- Records processed in log order
- No watermark tracking
- Simpler but less powerful for time-based analytics

**Why we skipped it:**
- KurrentDB's $all log is already ordered
- Primary use cases (projections, read models) don't need event-time windows
- Can be added later if needed

### 4.4 Single State Type

**Flink** has multiple state types:
- **Keyed State**: Scoped to current key (ValueState, ListState, MapState, etc.)
- **Operator State**: Scoped to operator instance (ListState, BroadcastState)

**KurrentDB** has only partitioned state:
- State is scoped to partition
- Simpler model covers most use cases
- User manages state structure

---

## 5. What We Changed

### 5.1 Worker Autonomy (Hive Mind)

**Flink (traditional mode):**
- JobManager controls task lifecycle
- Parallelism set at job submission
- Changing parallelism requires job restart

**KurrentDB:**
- Workers control their own lifecycle
- Workers connect/disconnect freely
- Parallelism changes by workers joining/leaving

**Why we changed it:**
- Simpler server implementation (no container orchestration)
- Users deploy workers however they want (K8s, VMs, bare metal)
- Natural fit for auto-scaling
- Similar to Flink Reactive Mode, but as the default

### 5.2 Partition Assignment Strategy

**Flink:**
- Key groups assigned at job start
- Complex rescheduling on failure
- Rescaling requires redistribution planning

**KurrentDB:**
- Partitions reassigned on any worker change
- Simple consistent hashing
- Checkpoint before reassignment ensures state availability

### 5.3 Checkpoint Triggering

**Flink:**
- Time-based interval (default 10 minutes, often configured to seconds)
- Can be triggered programmatically

**KurrentDB:**
- Time-based interval (default 30 seconds)
- Record count threshold
- Triggered on worker disconnect (graceful shutdown)
- Triggered when caught up to log head
- Manual savepoint trigger

### 5.4 State Storage: Shared Store vs RPC Transfer

**Flink:**
- State uploaded to checkpoint storage (HDFS, S3, etc.)
- State downloaded on recovery
- Server/coordinator involved in transfer

**KurrentDB:**
- State committed directly to shared store
- Workers claim partitions from shared store
- No upload/download RPCs needed
- Server only coordinates checkpoints, not state transfer

---

## 6. Feature Comparison Matrix

### Core Features

| Feature                 | Flink             | KurrentDB Processors | Notes                   |
|-------------------------|-------------------|----------------------|-------------------------|
| Distributed checkpoints | ✅                 | ✅                    | Both use Chandy-Lamport |
| Exactly-once state      | ✅                 | ✅                    | Via checkpoints         |
| At-least-once delivery  | ✅                 | ✅                    | Default guarantee       |
| Savepoints              | ✅                 | ✅                    | Manual checkpoints      |
| Elastic scaling         | ✅ (Reactive Mode) | ✅                    | Native in KurrentDB     |
| Keyed/partitioned state | ✅                 | ✅                    | State per key/partition |
| Checkpoint retention    | ✅                 | ✅                    | Configurable count      |

### Processing Model

| Feature                        | Flink | KurrentDB Processors | Notes                      |
|--------------------------------|-------|----------------------|----------------------------|
| Operator DAGs                  | ✅     | ❌                    | Not planned                |
| Multiple sources               | ✅     | ❌                    | Single source ($all)       |
| Multiple sinks                 | ✅     | ✅                    | Worker's responsibility    |
| Windowing                      | ✅     | ❌                    | Can implement in user code |
| Event time                     | ✅     | ❌                    | Processing time only       |
| Watermarks                     | ✅     | ❌                    | Not needed                 |
| CEP (Complex Event Processing) | ✅     | ❌                    | Not planned                |
| SQL/Table API                  | ✅     | ❌                    | Not planned                |

### Deployment & Operations

| Feature            | Flink          | KurrentDB Processors | Notes                      |
|--------------------|----------------|----------------------|----------------------------|
| Standalone cluster | ✅              | ❌                    | Embedded in KurrentDB      |
| Kubernetes native  | ✅              | ✅                    | Workers can run anywhere   |
| Worker management  | Server-managed | Self-managed         | Hive Mind model            |
| Job packaging      | JAR files      | Client SDK           | Different deployment model |
| Web UI             | ✅              | ❌                    | API only (for now)         |
| REST API           | ✅              | ✅                    | gRPC-based                 |
| Metrics            | ✅              | ✅                    | OpenTelemetry              |

### State Management

| Feature                 | Flink | KurrentDB Processors | Notes               |
|-------------------------|-------|----------------------|---------------------|
| RocksDB state backend   | ✅     | ✅                    | Pluggable           |
| Heap state backend      | ✅     | ✅                    | In-memory option    |
| Incremental checkpoints | ✅     | ❌                    | Full snapshots only |
| State TTL               | ✅     | ❌                    | User manages        |
| Queryable state         | ✅     | ❌                    | Not planned         |
| Broadcast state         | ✅     | ❌                    | Not applicable      |

### Fault Tolerance

| Feature            | Flink   | KurrentDB Processors | Notes                         |
|--------------------|---------|----------------------|-------------------------------|
| Automatic recovery | ✅       | ✅                    | Both recover from checkpoints |
| Checkpoint timeout | ✅       | ✅                    | Configurable                  |
| Failure policies   | ✅       | ✅                    | Continue or fail              |
| Exactly-once sinks | ✅ (2PC) | ❌                    | Worker's responsibility       |

---

## 7. Scaling Comparison

### Flink Scaling

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         FLINK SCALING                                    │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   Traditional Mode:                                                     │
│   ─────────────────                                                     │
│   1. Stop job                                                           │
│   2. Take savepoint                                                     │
│   3. Resubmit job with new parallelism                                 │
│   4. Restore from savepoint                                            │
│                                                                          │
│   Downtime: Minutes (depending on state size)                          │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Reactive Mode (since 1.13):                                           │
│   ───────────────────────────                                           │
│   1. Add/remove TaskManagers                                           │
│   2. Job automatically rescales                                        │
│   3. Checkpoint triggered                                              │
│   4. State redistributed                                               │
│                                                                          │
│   Downtime: Seconds                                                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### KurrentDB Scaling

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      KURRENTDB SCALING                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   Adding Workers:                                                       │
│   ───────────────                                                       │
│   1. New worker connects                                                │
│   2. Server triggers checkpoint                                         │
│   3. Server recalculates partition assignments                         │
│   4. Workers claim state for new partitions from shared store          │
│   5. Processing resumes                                                 │
│                                                                          │
│   Downtime: Seconds                                                    │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Removing Workers:                                                     │
│   ─────────────────                                                     │
│   1. Worker sends Disconnect                                           │
│   2. Server triggers checkpoint                                         │
│   3. Worker commits final state to shared store                        │
│   4. Server reassigns partitions to remaining workers                  │
│   5. Remaining workers claim state from shared store                   │
│   6. Processing resumes                                                 │
│                                                                          │
│   Downtime: Seconds                                                    │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Client-Side Parallelism (unique to KurrentDB):                        │
│   ───────────────────────────────────────────────                       │
│   • Run multiple workers in same process                               │
│   • Each worker = separate gRPC connection                             │
│   • Server doesn't know/care                                           │
│   • Two scaling dimensions: machines × workers-per-machine             │
│                                                                          │
│   Example: 4 machines × 4 local workers = 16 total workers             │
│            128 partitions ÷ 16 workers = 8 partitions each             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Scaling Comparison Summary

| Aspect                  | Flink (Traditional) | Flink (Reactive) | KurrentDB |
|-------------------------|---------------------|------------------|-----------|
| Scale up                | Job restart         | Automatic        | Automatic |
| Scale down              | Job restart         | Automatic        | Automatic |
| Scaling decision        | Explicit            | Automatic        | Automatic |
| Typical downtime        | Minutes             | Seconds          | Seconds   |
| Client-side parallelism | ❌                   | ❌                | ✅         |

---

## 8. Failure Handling Comparison

### Flink Failure Handling

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    FLINK FAILURE SCENARIOS                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   TaskManager Failure:                                                  │
│   ────────────────────                                                  │
│   1. JobManager detects heartbeat timeout                              │
│   2. JobManager marks tasks as failed                                  │
│   3. JobManager reschedules tasks to other TaskManagers                │
│   4. Tasks restore from last checkpoint                                │
│   5. Processing resumes from checkpoint position                       │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   JobManager Failure (HA mode):                                        │
│   ─────────────────────────────                                        │
│   1. ZooKeeper detects leader failure                                  │
│   2. New leader elected                                                │
│   3. New leader recovers job metadata from ZooKeeper                   │
│   4. New leader restarts job from last checkpoint                      │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Checkpoint Failure:                                                   │
│   ───────────────────                                                   │
│   Options:                                                              │
│   • Fail job (default for exactly-once sinks)                          │
│   • Continue processing (checkpoint skipped)                           │
│   • Tolerate N consecutive failures                                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### KurrentDB Failure Handling

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    KURRENTDB FAILURE SCENARIOS                           │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   Worker Failure:                                                       │
│   ───────────────                                                       │
│   1. Server detects connection drop                                    │
│   2. Server marks worker as disconnected                               │
│   3. Server reassigns partitions to remaining workers                  │
│   4. Remaining workers claim state from shared store                   │
│   5. Processing resumes from checkpoint position                       │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Server Failure:                                                       │
│   ───────────────                                                       │
│   KurrentDB cluster handles this:                                       │
│   1. Other cluster nodes detect failure                                │
│   2. Leader election (if leader failed)                                │
│   3. Workers reconnect to available nodes                              │
│   4. Processor state recovered from $processors-{id} stream            │
│   5. Processing resumes from last checkpoint                           │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Checkpoint Failure:                                                   │
│   ───────────────────                                                   │
│   Policy: CONTINUE (default)                                           │
│   • Disconnect failed workers                                          │
│   • Discard partial checkpoint                                         │
│   • Reassign partitions                                                │
│   • Resume from last good checkpoint                                   │
│                                                                          │
│   Policy: FAIL_PROCESSOR                                               │
│   • Disconnect all workers                                             │
│   • Processor goes idle                                                │
│   • Requires manual intervention                                       │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Failure Handling Comparison

| Scenario            | Flink                          | KurrentDB                   |
|---------------------|--------------------------------|-----------------------------|
| Worker/Task failure | JobManager reschedules         | Server reassigns partitions |
| Coordinator failure | ZooKeeper-based HA             | KurrentDB cluster handles   |
| Checkpoint timeout  | Configurable policy            | Configurable policy         |
| Network partition   | Complex (split-brain possible) | Simpler (single cluster)    |
| Recovery time       | Depends on state size          | Depends on state size       |

---

## 9. When to Use Which

### Use Apache Flink When:

| Scenario                       | Why Flink                                           |
|--------------------------------|-----------------------------------------------------|
| **Complex ETL pipelines**      | Operator DAGs enable sophisticated transformations  |
| **Multiple data sources**      | Native connectors for Kafka, files, databases, etc. |
| **Event-time processing**      | Watermarks and event-time windows                   |
| **SQL-based processing**       | Flink SQL and Table API                             |
| **Machine learning pipelines** | FlinkML, integration with ML frameworks             |
| **Existing Flink expertise**   | Team knows Flink already                            |
| **Complex CEP patterns**       | Flink CEP library                                   |
| **Multi-cluster federation**   | Need to process from multiple clusters              |

### Use KurrentDB Processors When:

| Scenario                     | Why KurrentDB                                    |
|------------------------------|--------------------------------------------------|
| **Already using KurrentDB**  | Native integration, no additional infrastructure |
| **Event sourcing patterns**  | Projections, read models, process managers       |
| **Simple processing needs**  | Don't need operator DAGs                         |
| **Operational simplicity**   | No separate cluster to manage                    |
| **Dynamic scaling needs**    | Workers join/leave freely                        |
| **Team knows .NET**          | Native .NET SDK                                  |
| **Want embedded processing** | Processing embedded in database                  |
| **Cost sensitivity**         | No additional infrastructure costs               |

### The Sweet Spot

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         DECISION GUIDE                                   │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   KurrentDB Processors is ideal for:                                    │
│                                                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                                                                  │   │
│   │   • Building projections from record streams                    │   │
│   │   • Maintaining read models                                     │   │
│   │   • Running process managers / sagas                            │   │
│   │   • Aggregating statistics                                      │   │
│   │   • Reacting to records (send email, call API)                 │   │
│   │   • Synchronizing to external systems                          │   │
│   │                                                                  │   │
│   │   Basically: "I have records in KurrentDB and want to          │   │
│   │              do something with them, reliably"                  │   │
│   │                                                                  │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│   ─────────────────────────────────────────────────────────────────     │
│                                                                          │
│   Consider Flink instead for:                                           │
│                                                                          │
│   ┌─────────────────────────────────────────────────────────────────┐   │
│   │                                                                  │   │
│   │   • Complex multi-source joins                                  │   │
│   │   • Event-time windowed aggregations                           │   │
│   │   • SQL-based stream processing                                │   │
│   │   • Machine learning pipelines                                 │   │
│   │   • CEP with complex patterns                                  │   │
│   │                                                                  │   │
│   │   Basically: "I need a full-featured stream processing         │   │
│   │              engine with complex transformations"               │   │
│   │                                                                  │   │
│   └─────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Summary

KurrentDB Processors takes Flink's battle-tested checkpointing mechanism and embeds it in a simpler architecture:

| Aspect         | What We Did                                                            |
|----------------|------------------------------------------------------------------------|
| **Borrowed**   | Chandy-Lamport checkpointing, barriers, savepoints, exactly-once state |
| **Simplified** | No barrier alignment, no operator DAGs, no watermarks                  |
| **Changed**    | Worker autonomy (Hive Mind), single source, embedded in database       |
| **Result**     | Simpler system for KurrentDB-native stream processing                  |

The goal isn't to replace Flink—it's to provide "just enough" stream processing for teams already using KurrentDB, without the operational overhead of a separate cluster.

**For Flink users joining the team:** You'll recognize the checkpointing concepts. The main mental shift is from "submit a job with an operator DAG" to "connect workers that process records directly."
