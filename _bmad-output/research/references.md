---
version: '1.0'
date: '2025-12-25'
status: 'Draft'
author: 'Sergio'
---

# Kurrent Processors - Research and References

## Table of Contents

1. [Chandy-Lamport Algorithm](#1-chandy-lamport-algorithm)
2. [Comparison with Apache Flink](#2-comparison-with-apache-flink)
3. [Comparison with Other Systems](#3-comparison-with-other-systems)
4. [Design Decisions](#4-design-decisions)
5. [Academic References](#5-academic-references)

---

## 1. Chandy-Lamport Algorithm

### 1.1 Background

The **Chandy-Lamport distributed snapshot algorithm** (1985) solves the fundamental problem of capturing a consistent global snapshot of a distributed system without halting operations.

### 1.2 Original Algorithm Assumptions

| Assumption | Description |
|------------|-------------|
| Message passing | Processes communicate via messages |
| FIFO channels | Messages arrive in order sent |
| Reliable channels | No message loss |
| Strongly connected | All processes reachable |

### 1.3 How It Works

1. **Initiator** sends a **marker** (checkpoint trigger)
2. When a process receives the marker:
   - It records its **local state**
   - It forwards the marker to all outgoing channels
   - It starts recording **incoming messages** on each channel
3. Snapshot is complete when all processes have received markers on all channels

### 1.4 KurrentDB Adaptation

KurrentDB has a **simpler topology** than general distributed systems:

| Chandy-Lamport Concept | KurrentDB Implementation |
|------------------------|--------------------------|
| Marker message | `CheckpointBarrier` |
| Process state | Worker state (per partition) |
| Channel state | Not needed (single source log) |
| Initiator | Server (Checkpoint Coordinator) |

**Key insight**: There's a **single source log** ($all) that feeds all workers—no complex fan-in of multiple channels. Workers simply:

1. Receive checkpoint barrier
2. Finish current record
3. Commit state to shared store
4. Send `CheckpointComplete` with positions

---

## 2. Comparison with Apache Flink

### 2.1 Architecture Comparison

| Aspect | Apache Flink | Kurrent Processors |
|--------|--------------|---------------------|
| **Deployment** | JobManager deploys TaskManagers | Workers are autonomous (Hive Mind) |
| **Parallelism** | Set at job submission | Workers join/leave freely |
| **State backend** | RocksDB, HashMap, external | Shared state store (pluggable) |
| **Checkpoint storage** | FileSystem, S3, etc. | Workers commit directly to shared store |
| **Operator model** | DAG of operators | Single `IRecordProcessor` |

### 2.2 Checkpointing Comparison

| Aspect | Apache Flink | Kurrent Processors |
|--------|--------------|---------------------|
| **Algorithm** | Chandy-Lamport with barriers | Chandy-Lamport with barriers |
| **Barrier alignment** | Required (multiple channels) | Not needed (single source) |
| **State snapshot** | Operator → State Backend | Worker → Shared Store |
| **Acknowledgment** | TaskManager → JobManager | Worker → Server |

### 2.3 Flink Reactive Mode (2021)

Flink's **Reactive Mode** is similar to our Hive Mind model:

| Feature | Flink Reactive | Kurrent Hive Mind |
|---------|----------------|-------------------|
| Workers manage themselves | ✓ | ✓ |
| Dynamic parallelism | ✓ | ✓ |
| Server coordinates | ✓ | ✓ |
| Server deploys workers | ✗ | ✗ |

### 2.4 Why Not Barrier Alignment?

Flink uses **barrier alignment** when an operator has multiple input channels:

```
Channel A: [r1, r2, BARRIER, r3, r4]
Channel B: [r5, BARRIER, r6, r7]

Operator must:
1. Process r1, r2, r5
2. Wait for BARRIER on both channels
3. Snapshot state
4. Continue with r3, r4, r6, r7
```

**KurrentDB doesn't need this** because:
- Single source log ($all) feeds all workers
- No fan-in at worker level
- Each worker has one input stream

---

## 3. Comparison with Other Systems

### 3.1 Apache Kafka Consumer Groups

| Aspect | Kafka Consumer Groups | Kurrent Processors |
|--------|----------------------|---------------------|
| **Partitioning** | Topic partitions | Key groups (partitions) |
| **Rebalancing** | On consumer join/leave | On worker join/leave |
| **Offset tracking** | Consumer commits offsets | Server tracks via checkpoint |
| **State management** | External (user's responsibility) | Shared state store |
| **Checkpointing** | Manual offset commit | Coordinated barriers |

### 3.2 Apache Pulsar Subscriptions

| Aspect | Pulsar Subscriptions | Kurrent Processors |
|--------|---------------------|---------------------|
| **Types** | Exclusive, Shared, Failover, Key_Shared | Key_Shared-like (by partition) |
| **Ordering** | Per-key with Key_Shared | Per-partition |
| **State** | Pulsar Functions state | Shared state store |
| **Checkpointing** | Cursor acknowledgment | Chandy-Lamport barriers |

### 3.3 Feature Matrix

| Feature | Kafka | Pulsar | Flink | Kurrent |
|---------|-------|--------|-------|---------|
| At-least-once delivery | ✓ | ✓ | ✓ | ✓ |
| Exactly-once state | ✗ | ✗ | ✓ | ✓ |
| Coordinated checkpoints | ✗ | ✗ | ✓ | ✓ |
| Elastic scaling | ✓ | ✓ | ✓ | ✓ |
| Autonomous workers | ✗ | ✗ | Reactive | ✓ |
| Built-in state store | ✗ | Functions | RocksDB | Shared |

---

## 4. Design Decisions

### 4.1 Shared State Store vs RPC Transfer

**Considered**: Workers upload/download state via server RPCs.

**Chosen**: Workers commit state directly to shared store.

| RPC Transfer | Shared Store |
|--------------|--------------|
| Server handles all state | Workers access directly |
| Upload/download latency | Commit/claim latency |
| Server bottleneck | Store scales independently |
| State serialization overhead | Store handles format |

**Rationale**: Eliminates server as bottleneck for state, simplifies protocol, enables larger state sizes.

### 4.2 Server-Hosted vs Autonomous Workers

**Considered**: Server deploys and manages worker processes.

**Chosen**: Workers are autonomous (Hive Mind model).

| Server-Hosted | Autonomous |
|---------------|------------|
| Server controls lifecycle | Workers control lifecycle |
| Container orchestration needed | Existing deployment tools |
| Less deployment flexibility | Full deployment flexibility |

**Rationale**: Simpler server, users manage deployment with existing tools, can add server-hosted mode later.

### 4.3 Barrier Alignment vs No Alignment

**Considered**: Flink-style barrier alignment with buffering.

**Chosen**: No alignment needed.

| With Alignment | Without Alignment |
|----------------|-------------------|
| Multiple input channels | Single source log |
| Buffering during checkpoint | No buffering |
| Complex state machine | Simple: barrier → commit → ack |

**Rationale**: KurrentDB's single-source topology eliminates the need for alignment.

### 4.4 Full State vs Incremental Checkpoints

**Considered**: Upload only changed state since last checkpoint.

**Deferred**: Full state upload on each checkpoint.

| Incremental | Full State |
|-------------|------------|
| Less data transfer | More data transfer |
| Complex diffing | Simple snapshot |
| Depends on state format | Format-agnostic |

**Rationale**: Simpler implementation. Shared store can optimize internally. Can add incremental later.

### 4.5 Exactly-Once Sinks

**Considered**: Built-in transactional writes to external systems.

**Rejected**: Worker's responsibility.

| Built-in | Worker Responsibility |
|----------|----------------------|
| Tight coupling to sinks | Loose coupling |
| Complex transaction coord | Simple protocol |
| Limited sink support | Any sink via idempotency |

**Rationale**: Each sink has different semantics. Workers can implement idempotency patterns.

---

## 5. Academic References

### 5.1 Primary Papers

1. **Chandy, K.M. and Lamport, L. (1985)**
   "Distributed Snapshots: Determining Global States of Distributed Systems"
   ACM Transactions on Computer Systems, 3(1), 63-75.
   [DOI:10.1145/214451.214456](https://doi.org/10.1145/214451.214456)

   *Foundation for our checkpoint mechanism.*

2. **Carbone, P., et al. (2015)**
   "Lightweight Asynchronous Snapshots for Distributed Dataflows"
   arXiv:1506.08603
   [arXiv:1506.08603](https://arxiv.org/abs/1506.08603)

   *Flink's adaptation of Chandy-Lamport for stream processing.*

### 5.2 System Documentation

| System | Documentation |
|--------|---------------|
| Apache Flink | [Checkpointing](https://nightlies.apache.org/flink/flink-docs-stable/docs/ops/state/checkpoints/) |
| Apache Flink | [Reactive Mode](https://flink.apache.org/2021/05/03/announcing-the-release-of-apache-flink-1.13/) |
| Apache Kafka | [Consumer Groups](https://kafka.apache.org/documentation/#consumerconfigs) |
| Apache Pulsar | [Subscriptions](https://pulsar.apache.org/docs/concepts-messaging/#subscriptions) |

### 5.3 Related Concepts

| Concept | Description |
|---------|-------------|
| **Consistent Hashing** | Algorithm for distributing partitions with minimal redistribution when workers change |
| **FIFO** | First-In-First-Out ordering required by Chandy-Lamport |
| **Global Consistent Cut** | The barrier position where we know exactly what state corresponds to what input |
| **At-Least-Once** | Delivery guarantee where records may be delivered multiple times but never lost |
| **Exactly-Once State** | State reflects each record exactly once, even if delivered multiple times |

---

## Appendix A: Glossary

| Term | Definition |
|------|------------|
| **Processor** | Logical grouping of workers with same processor_id |
| **Worker** | Autonomous process that connects to process records |
| **Partition** | Subdivision of key space (0 to max_parallelism-1) |
| **Checkpoint** | Consistent snapshot of all workers' positions |
| **Savepoint** | Manually-triggered checkpoint (never auto-deleted) |
| **Barrier** | Marker message triggering checkpoint |
| **Hive Mind** | Model where workers are autonomous agents |
| **Rebalancing** | Redistributing partitions when workers change |
| **Shared State Store** | Technology-agnostic storage accessible by all workers |

---

## Appendix B: Superseded Documents

The following documents have been superseded by this BMAD documentation:

| Document | Location | Status |
|----------|----------|--------|
| Original Plan | `.project/specs/001-processors/plan.md` | Archive |
| Original Spec | `.project/specs/001-processors/spec.md` | Archive |
| Original Protocol | `.project/specs/001-processors/contracts/protocol.md` | Archive |
| Original Research | `.project/specs/001-processors/research/` | Archive |
| OpenSpec Spec | `openspec/changes/add-processors/specs/processors/spec.md` | Archive |
| OpenSpec Protocol | `openspec/changes/add-processors/contracts/protocol.md` | Archive |

**Canonical BMAD documents:**

| Document | Location |
|----------|----------|
| PRD | `_bmad-output/prd.md` |
| Architecture | `_bmad-output/architecture.md` |
| Protocol | `_bmad-output/contracts/protocol.md` |
| References | `_bmad-output/research/references.md` |
