# Parallelism and Key Groups in KurrentDB Processors

## Overview

KurrentDB Processors use a **key group** model for distributing stateful processing across multiple workers. This model, inspired by Apache Flink's approach, enables elastic scaling while maintaining exactly-once processing guarantees.

---

## Core Concepts

### Parallelism vs Max Parallelism

| Concept             | Description                                                                                 |
|---------------------|---------------------------------------------------------------------------------------------|
| **Parallelism**     | The *actual* number of workers currently processing. Dynamic—changes as workers join/leave. |
| **Max Parallelism** | The *maximum* number of workers that can ever process this subscription. Fixed at creation. |

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│  max_parallelism = 128 (fixed at processor creation)                    │
│                                                                          │
│  Time 0:  2 workers connected    → parallelism = 2                      │
│  Time 1:  4 workers connected    → parallelism = 4                      │
│  Time 2:  1 worker crashes       → parallelism = 3                      │
│  Time 3:  10 workers connected   → parallelism = 10                     │
│                                                                          │
│  Can scale up to 128 workers without any reconfiguration.              │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Key Groups (Partitions)

A **key group** (also called partition) is a unit of state assignment. Records are assigned to key groups by hashing their partition key:

```
key_group = hash(partition_key) % max_parallelism
```

Key groups are then distributed across available workers:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                                                                          │
│  max_parallelism = 8 (8 key groups: 0, 1, 2, 3, 4, 5, 6, 7)            │
│                                                                          │
│  With 2 workers:                                                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Worker A: key groups [0, 1, 2, 3]                                │   │
│  │  Worker B: key groups [4, 5, 6, 7]                                │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  With 4 workers:                                                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Worker A: key groups [0, 1]                                      │   │
│  │  Worker B: key groups [2, 3]                                      │   │
│  │  Worker C: key groups [4, 5]                                      │   │
│  │  Worker D: key groups [6, 7]                                      │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
│  With 8 workers:                                                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Worker A: key group [0]                                          │   │
│  │  Worker B: key group [1]                                          │   │
│  │  Worker C: key group [2]                                          │   │
│  │  ... etc                                                          │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Why Key Groups?

### The Problem Without Key Groups

Without key groups, scaling would require rehashing all state:

```
┌─────────────────────────────────────────────────────────────────────────┐
│  BAD: Direct hash to worker count                                       │
│                                                                          │
│  2 workers: hash(key) % 2                                               │
│  Customer "alice" → hash % 2 = 0 → Worker A                            │
│                                                                          │
│  Scale to 3 workers: hash(key) % 3                                      │
│  Customer "alice" → hash % 3 = 2 → Worker C  ← MOVED!                  │
│                                                                          │
│  ALL state must be redistributed! Expensive and error-prone.           │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### The Solution With Key Groups

With key groups, only key group assignments change:

```
┌─────────────────────────────────────────────────────────────────────────┐
│  GOOD: Hash to key groups, assign key groups to workers                │
│                                                                          │
│  max_parallelism = 128                                                  │
│  Customer "alice" → hash % 128 = 42 → key_group 42                     │
│                                                                          │
│  2 workers:                                                             │
│  Key group 42 assigned to Worker A (handles groups 0-63)               │
│  Customer "alice" → Worker A                                            │
│                                                                          │
│  Scale to 3 workers:                                                    │
│  Key group 42 still assigned to Worker A (handles groups 0-42)         │
│  Customer "alice" → Worker A  ← NO CHANGE!                             │
│                                                                          │
│  Only ~1/3 of key groups move when adding a worker.                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Scaling Behavior

KurrentDB Processors use **reactive scaling** (similar to Flink Reactive Mode):

### Scale Up (Worker Joins)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    WORKER JOINS                                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Before: Workers A, B (each handles 64 key groups)                      │
│  Event:  Worker C connects                                              │
│                                                                          │
│  1. Server triggers a barrier (checkpoint) to all workers              │
│  2. Workers A and B commit their state to shared store                 │
│  3. Server computes new assignment:                                     │
│     - Worker A: key groups 0-42                                        │
│     - Worker B: key groups 43-84                                       │
│     - Worker C: key groups 85-127                                      │
│  4. Worker C claims key groups 85-127 from shared store                │
│  5. Processing resumes with 3 workers                                  │
│                                                                          │
│  Workers A and B continue with reduced key groups.                     │
│  Worker C catches up from checkpoint.                                  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Scale Down (Worker Leaves Gracefully)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    WORKER DISCONNECTS (GRACEFUL)                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Before: Workers A, B, C                                                │
│  Event:  Worker C sends Disconnect                                      │
│                                                                          │
│  1. Server triggers a barrier to all workers                           │
│  2. All workers commit state (including C) to shared store             │
│  3. Server acknowledges C's disconnect                                  │
│  4. Server computes new assignment:                                     │
│     - Worker A: key groups 0-63                                        │
│     - Worker B: key groups 64-127                                      │
│  5. Workers A and B receive WorkAssigned with new key groups           │
│  6. Workers A and B claim state for their new key groups               │
│  7. Processing resumes with 2 workers                                  │
│                                                                          │
│  Graceful: Checkpoint ensures no data loss.                            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Worker Crashes (Ungraceful)

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    WORKER CRASHES                                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Before: Workers A, B, C                                                │
│  Event:  Worker C stops heartbeating (crash/network partition)         │
│                                                                          │
│  1. Server detects missing heartbeat (timeout)                         │
│  2. Server triggers barrier to remaining workers (A, B)                │
│  3. Workers A and B commit state to shared store                       │
│  4. Server reassigns C's key groups to A and B                         │
│  5. Workers A and B claim state for new key groups from shared store   │
│  6. Processing resumes from last checkpoint                            │
│                                                                          │
│  Records processed by C since last checkpoint are replayed.            │
│  Exactly-once semantics maintained via checkpoint recovery.            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Choosing Max Parallelism

### Default Value

The default `max_parallelism` is **128**, which is suitable for most use cases.

### Guidelines

| Scenario                             | Recommended Value |
|--------------------------------------|-------------------|
| Small deployment (1-4 workers)       | 128 (default)     |
| Medium deployment (5-32 workers)     | 128 (default)     |
| Large deployment (33-128 workers)    | 128 or 256        |
| Very large deployment (129+ workers) | 256 or 512        |

### Considerations

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    CHOOSING MAX_PARALLELISM                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Too Low:                                                               │
│  - Cannot scale beyond max_parallelism workers                         │
│  - Must reprocess all data to increase (cannot change after creation)  │
│                                                                          │
│  Too High:                                                              │
│  - Slightly more metadata overhead (negligible)                        │
│  - State files include key_group column (minimal space)                │
│  - More granular rebalancing (actually a benefit)                      │
│                                                                          │
│  Rule of Thumb:                                                         │
│  Set to at least 2x your expected maximum worker count.                │
│  When in doubt, use 128 or 256.                                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### Immutability

**Max parallelism cannot be changed after processor creation.**

Why? Because state is stored with key group assignments:

```
State file (Parquet):
┌───────────┬─────────────┬─────────────┐
│ key_group │ key         │ value       │
├───────────┼─────────────┼─────────────┤
│ 42        │ cust:alice  │ {...}       │
│ 17        │ cust:bob    │ {...}       │
│ 42        │ cust:carol  │ {...}       │
└───────────┴─────────────┴─────────────┘

key_group = hash(key) % max_parallelism

If max_parallelism changes, all key_group values would be wrong!
```

To change max_parallelism, you must:
1. Create a new processor with different max_parallelism
2. Reprocess from the beginning (or migrate state manually)

---

## State Storage and Key Groups

### How State is Stored

Each worker's state includes the key_group for efficient filtering:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    STATE STORAGE                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Shared State Store:                                                    │
│  ┌───────────┬─────────────┬─────────────┬──────────────┐              │
│  │ key_group │ key         │ value       │ updated_at   │              │
│  ├───────────┼─────────────┼─────────────┼──────────────┤              │
│  │ 12        │ cust:alice  │ {...}       │ 1234567890   │              │
│  │ 42        │ cust:carol  │ {...}       │ 1234567891   │              │
│  │ 12        │ cust:dave   │ {...}       │ 1234567892   │              │
│  └───────────┴─────────────┴─────────────┴──────────────┘              │
│                                                                          │
│  key_group is computed automatically:                                   │
│  key_group = hash(key) % max_parallelism                               │
│                                                                          │
│  Workers only access partitions they own.                              │
│  On checkpoint, state is committed to shared store.                    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### State Claiming During Rebalancing

When key groups move between workers:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    STATE CLAIMING                                        │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Worker C joins, gets assigned key groups 85-127                       │
│                                                                          │
│  1. Worker C calls ClaimPartitionsAsync([85-127], checkpointId)        │
│                                                                          │
│  2. Shared store:                                                       │
│     - Worker C gains access to partitions 85-127                       │
│     - State already exists in shared store                             │
│     - No transfer needed - just claim ownership                        │
│                                                                          │
│  3. Worker C sends WorkAccepted, starts processing                     │
│                                                                          │
│  Efficient: No upload/download - workers access shared store directly. │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Subscription Types and Parallelism

### EXCLUSIVE

- Single worker only
- `max_parallelism` is ignored
- Simplest model, no distribution

### SHARED (Round-Robin)

- Multiple workers, round-robin distribution
- `max_parallelism` is ignored
- No state locality guarantees
- Use for stateless processing only

### KEY_SHARED

- Multiple workers, partitioned by key
- `max_parallelism` determines key groups
- State locality: same key → same worker
- Use for stateful processing

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SUBSCRIPTION TYPE COMPARISON                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│            │ EXCLUSIVE │ SHARED      │ KEY_SHARED                       │
│  ──────────┼───────────┼─────────────┼──────────────────────────────    │
│  Workers   │ 1         │ Many        │ Many                             │
│  Ordering  │ Global    │ None        │ Per-key                          │
│  State     │ All local │ No locality │ Per-key locality                │
│  Scaling   │ None      │ Linear      │ Linear (up to max_parallelism)  │
│  Use case  │ Simple    │ Stateless   │ Stateful                        │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Example Configuration

```protobuf
ProcessorSettings {
    subscription: {
        filter: "stream='$all'"
        type: KEY_SHARED
        partition_key: "data.customerId"
        start_position: { beginning: {} }
    }
    batching: {
        batch_size: 500
        batch_timeout: { seconds: 0, nanos: 100000000 }  // 100ms
    }
    checkpoint: {
        interval: { seconds: 30 }
        event_count: 10000
        retention_count: 5
    }
    max_parallelism: 128
}
```

This configuration:
- Reads all records from `$all`
- Partitions by `customerId` field in event data
- Supports up to 128 parallel workers
- Checkpoints every 30 seconds or 10,000 records
- Keeps 5 checkpoints for recovery

---

## Comparison with Apache Flink

| Aspect             | Apache Flink                        | KurrentDB Processors      |
|--------------------|-------------------------------------|---------------------------|
| Max Parallelism    | Configurable, default 128           | Configurable, default 128 |
| Parallelism        | Fixed at job submission             | Dynamic (reactive mode)   |
| Key Groups         | Hash-based                          | Hash-based                |
| Scaling            | Requires restart (or reactive mode) | Always reactive           |
| State Backend      | RocksDB, Memory, etc.               | Shared Store (pluggable)  |
| Checkpoint Storage | HDFS, S3, etc.                      | Shared Store              |

KurrentDB Processors are most similar to **Flink in Reactive Mode**, where parallelism automatically adjusts to available workers.
