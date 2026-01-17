---
stepsCompleted: [1, 2]
inputDocuments:
  - _bmad-output/research/references.md
  - _bmad-output/research/checkpointing-guide.md
  - _bmad-output/research/flink-comparison.md
  - _bmad-output/research/parallelism-key-groups.md
  - _bmad-output/research/design.md
  - _bmad-output/research/checkpointing-origin.md
  - _bmad-output/research/position-model.md
workflowType: 'product-brief'
lastStep: 2
project_name: 'Kurrent.Client'
user_name: 'Sergio'
date: '2026-01-16'
---

# Product Brief: Kurrent Processors

## Executive Summary

Our subscription API is a support liability and an adoption blocker. Developers must build their own checkpointing, retry logic, and failure handling before they can do anything useful. The result: lost events, duplicate processing, and a support team answering the same questions repeatedly.

**Kurrent Processors fixes this.** It replaces the subscription complexity with a simple `IRecordProcessor` interface—one required method—while the SDK handles checkpointing, failure recovery, and elastic scaling automatically.

**Before (current subscriptions):**
```csharp
await foreach (var message in client.SubscribeToAll(position, filterOptions)) {
    switch (message) {
        case StreamMessage.Event(var evnt):
            await ProcessEvent(evnt);
            checkpoint = evnt.OriginalPosition;
            await SaveCheckpoint(checkpoint);  // Where? How often? What format?
            break;
        case StreamMessage.CaughtUp:
        case StreamMessage.AllStreamCheckpointReached:
            // ???
            break;
    }
}
// Plus: reconnection logic, error handling, position recovery on restart...
```

**After (Kurrent Processors):**
```csharp
await client.StartProcessorAsync("my-processor", new MyProcessor(), ct);
```

The implementation uses the **Chandy-Lamport distributed snapshot algorithm**—the same approach that powers Apache Flink's checkpointing at petabyte scale—adapted for KurrentDB's simpler single-source-log topology. No barrier alignment complexity. No JobManager bottleneck.

**Key capabilities:**
- **3-4 lines to start**: Convention over configuration. Sensible defaults.
- **Elastic scaling**: Add workers to scale up, remove them to scale down. No configuration changes.
- **Fault tolerance**: Automatic recovery from worker failures. At-least-once delivery (MVP).
- **Zero infrastructure change**: Works with existing KurrentDB deployments via LegacyGateway.

---

## Core Vision

### Problem Statement

The current subscription API exposes implementation complexity that should be hidden:

1. **No built-in checkpointing** — developers must implement position tracking, storage, and recovery
2. **No resilience patterns** — retry logic, backoff, reconnection all left to the user
3. **No coordination** — multiple workers can't share work without custom load balancing
4. **Complex message handling** — `CaughtUp`, `CheckpointReached`, `SubscriptionDropped` callbacks with unclear semantics

This isn't a documentation problem. The API surface reflects underlying complexity that should be the SDK's job to hide.

Projects like [Eventuous](https://eventuous.dev/) exist specifically to wrap our subscription API with checkpoint stores and handler abstractions. This demonstrates real demand for simplification that we should address natively.

### Problem Impact

**For a single worker:**
- Manual checkpoint storage decisions (file? database? Redis?)
- Manual reconnection logic with backoff
- Manual error handling and recovery
- Manual position tracking on restart

**For multiple workers (not currently possible):**
- No coordination primitives
- No consistent checkpoints across workers
- No automatic partition assignment
- No state transfer during scaling

**The result:**
- Developers build fragile custom frameworks
- Support team answers the same questions repeatedly
- Users evaluate alternatives that provide these capabilities out of the box

### The Six Problems We Solve

| # | Problem | Solution | How It Works |
|---|---------|----------|--------------|
| 1 | **Distributing Stateful Work** | Key Groups | 128 virtual partitions; keys hash to partitions, partitions assign to workers. State follows partition during rebalancing. |
| 2 | **Recovering from Failures** | Chandy-Lamport Checkpoints | Barrier-based snapshots at consistent cut points. On failure: restore checkpoint, replay from that position. |
| 3 | **Scaling Without Downtime** | Hive Mind Model | Checkpoint → reassign partitions → transfer state → resume. Total downtime: seconds, not minutes. |
| 4 | **Coordinating Distributed Checkpoints** | Barrier Protocol | Server broadcasts barrier to all workers. Each finishes current record, snapshots state, acknowledges. Checkpoint completes when all ack. |
| 5 | **Managing Worker State** | Pluggable IStateStore | Interface with DuckDB, in-memory, RocksDB implementations. Automatic snapshot/restore during checkpoints. |
| 6 | **Handling Checkpoint Failures** | Failure Policies | Configurable: continue without failed worker (reassign partitions), or fail entire processor. |

*For detailed explanations including diagrams, code examples, and edge cases, see the [Processors Design Document](.project/reference/features/processors.md).*

### Proposed Solution

**The API — `IRecordProcessor`:**
```csharp
public interface IRecordProcessor {
    // Only required method
    ValueTask ProcessRecords(ProcessRecordsContext context);

    // Optional lifecycle hooks (default: no-op)
    ValueTask OnInitialize(InitializationContext context) => ValueTask.CompletedTask;
    ValueTask OnCheckpointComplete(CheckpointCompleteContext context) => ValueTask.CompletedTask;
    ValueTask OnShutdown(ShutdownContext context) => ValueTask.CompletedTask;
}
```

**What the SDK handles:**
- Position tracking per partition
- Checkpoint coordination (Chandy-Lamport barriers)
- State serialization and storage
- Worker registration and partition assignment
- Reconnection with backoff
- Graceful shutdown with final checkpoint

**What users handle:**
- Business logic in `ProcessRecords()`
- (Optional) Lifecycle hooks for logging/metrics

### The Hive Mind Model

Unlike traditional job schedulers (Flink JobManager, Kafka consumer group coordinator), Kurrent Processors uses an autonomous worker model:

| Traditional Coordinator | Kurrent Hive Mind |
|------------------------|-------------------|
| Central coordinator deploys workers | Workers start themselves |
| Coordinator is single point of failure | No central coordinator to fail |
| Fixed parallelism requires reconfiguration | Dynamic parallelism—just add workers |
| Coordinator controls worker lifecycle | Workers decide when to join/leave |

**How it works:**
- Workers connect to a processor group and receive partition assignments
- Server coordinates checkpoints by broadcasting barriers to all workers
- When a worker joins: checkpoint → reassign partitions → transfer state → resume
- When a worker leaves: remaining workers continue, partitions reassigned on next checkpoint
- No rebalancing storms. No split-brain scenarios.

### Gateway Abstraction

Kurrent Processors introduces the `IProcessorGateway` interface to abstract the communication protocol:

```csharp
public interface IProcessorGateway : IAsyncDisposable {
    ValueTask<IWorkerSession> ConnectAsync(ProcessorSettings settings, CancellationToken ct);
}

public interface IWorkerSession : IAsyncEnumerable<ProcessorControlMessage>, IAsyncDisposable {
    SessionId SessionId { get; }
    ValueTask SendAsync(WorkerMessage message);
}
```

**Implementations:**
- `LegacyGateway`: Works over V1 protocol—enables V2 Preview without server changes
- `GrpcGateway` (future): Native V2 server protocol when available

User code doesn't change between gateways. This is how "zero infrastructure change" becomes possible.

---

## Foundation: The V2 Client

Kurrent Processors builds on foundational capabilities introduced in the V2 client:

### SQL-Based Filter Expressions

DuckDB-compatible WHERE clause syntax for filtering records:

```csharp
// Complex filtering with SQL syntax
var filter = RecordFilter.ByExpression(
    "stream LIKE 'order-%' AND properties.region IN ('us-east', 'us-west') AND timestamp >= '2024-01-01'"
);
```

Processors uses filter expressions for:
- Selecting which records to process
- Partition key extraction (`properties.customerId`, `stream`, SQL expressions)
- Server-side vs client-side filter optimization

### Automatic Serialization with Schema Registry

Zero-configuration SerDe for .NET types:
- Automatic schema registration and versioning
- Serialization/deserialization handled transparently
- Format negotiation (JSON, Protobuf, Avro)

Processors inherits this—users implement `ProcessRecords()` with typed records, not raw bytes.

### Built-in Resilience

The V2 client embeds resilience patterns that Processors leverages:
- Automatic reconnection with exponential backoff
- Connection health monitoring
- Graceful degradation under load

### The Record Model

The unified `Record` type provides:
- Consistent structure across all operations
- Rich metadata (properties, timestamps, positions, schema info)
- Foundation for `RecordBatch` passed to processors

### Fresh API Surface

The V2 client's simplified API establishes patterns that Processors follows:
- Consistent naming and ubiquitous language
- Builder patterns for configuration
- Async-first design throughout

---

## Delivery Guarantees

| Guarantee | Status | Implementation |
|-----------|--------|----------------|
| At-least-once delivery | ✅ MVP | Records may replay after recovery; design for idempotency |
| Exactly-once state | 🗓️ Post-MVP (~3 weeks) | Requires distributed state store implementation |
| Exactly-once to sinks | ⚠️ User responsibility | Workers implement idempotent writes to external systems |

**Note on exactly-once:** The architecture fully supports it. Chandy-Lamport provides consistent distributed snapshots. What's pending is the distributed state store implementation—not a design gap, just implementation sequencing.

---

## Competitive Position

| Capability | Kafka Streams | Apache Flink | Kurrent Processors |
|------------|---------------|--------------|-------------------|
| Automatic checkpointing | ✓ | ✓ | ✓ |
| Multi-worker coordination | ✓ | ✓ | ✓ |
| Elastic scaling | Manual | ✓ (Reactive Mode) | ✓ (Hive Mind) |
| Fault tolerance | ✓ | ✓ | ✓ |
| At-least-once | ✓ | ✓ | ✓ (MVP) |
| Exactly-once state | ✓ | ✓ | Post-MVP |
| No infrastructure change | ✗ (requires Kafka) | ✗ (requires cluster) | ✓ |
| Native event sourcing | ✗ | ✗ | ✓ |

**Our unique position:** Kafka/Flink-level capabilities without infrastructure change. Users keep their existing KurrentDB deployment and gain distributed stream processing through an SDK upgrade.

---

## Key Differentiators

### No Single Point of Failure

Workers are autonomous. They connect, receive work via checkpoint barriers, snapshot their state, and continue processing. When a worker dies, remaining workers continue uninterrupted. When it reconnects, it restores from the last checkpoint and resumes. The system converges to consistency through distributed coordination, not centralized control.

### No Infrastructure Leap

Kafka Streams requires Kafka. Flink requires a cluster. Kurrent Processors works with **any existing KurrentDB deployment**—cloud or self-hosted, single-node or cluster.

This isn't just developer convenience—it's a procurement story. An SDK upgrade is an engineering decision. New infrastructure is a purchasing decision with months of evaluation, security review, and budget approval.

### Gateway Abstraction for Graceful Evolution

The `IProcessorGateway` interface means the protocol can evolve without breaking user code. Today's `LegacyGateway` enables immediate adoption. Tomorrow's native V2 server support slots in seamlessly.

### Native Event Sourcing Integration

Unlike Kafka Streams (log compaction model) or Flink (external state backends), Kurrent Processors works with KurrentDB's append-only log and stream model natively. Event sourcing patterns don't require adaptation or compromise.

---

## Why Now

**1. Technical debt is compounding.**
Every month we don't fix subscriptions, more users build fragile custom frameworks. The support burden grows. The workarounds accumulate. The documentation gets more convoluted.

**2. V2 rewrite is the window.**
We're already touching this code. The new client architecture—filter expressions, SerDe, resilience, Record model—provides the foundation. Adding Processors now costs X. Retrofitting it later costs 3X.

**3. Competitive parity is table stakes.**
If we ship V2 without distributed processing capabilities, we're shipping something perceived as *worse* than Kafka Streams for stream processing workloads. That's not a V2—that's a regression in capability perception.

**4. The rebranding creates expectation.**
EventStoreDB → KurrentDB signals evolution and ambition. Users will expect capabilities that match. Processors delivers on that promise.

**5. The problem is validated.**
Third-party libraries like Eventuous exist specifically to simplify our subscriptions. The demand is proven. We should meet it natively—and exceed it with distribution.
