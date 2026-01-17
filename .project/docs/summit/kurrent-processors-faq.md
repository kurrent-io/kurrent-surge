# Kurrent Processors
## Anticipated Questions & Pushback FAQ V2

**Purpose:** Quick-reference for summit Q&A, organized by expected challenge type.

### Version History

| Version | Date       | Changes |
|---------|------------|---------|
| 1.0     | 2026-01-16 | Initial draft |
| 2.0     | 2026-01-16 | Restructured around 6-problem narrative; added Client V2 category |
| 2.1     | 2026-01-16 | Clarified Autonomous Mode: records from KurrentDB, coordination via State Store |

---

## Category 1: "What's the Relationship to Client V2?"

### Q: "Is Processors part of Client V2 or a separate thing?"

**One-liner:** "Processors is the flagship feature of Client V2—built on V2's game-changing capabilities."

**Full answer:**

Client V2 provides platform capabilities. Processors is the first major feature built on that platform.

**V2 provides:**
- SQL-based filter expressions
- Automatic SerDe with Schema Registry (zero config)
- Built-in connection resilience
- New ubiquitous language (Records, Messages, Schemas)

**Processors adds:**
- Gateway abstraction (why we ship without server changes)
- Chandy-Lamport checkpoint protocol
- Worker coordination model (Hive Mind)

The Gateway abstraction is Processors-specific architecture, not a V2 feature. We built it *using* V2's patterns.

---

### Q: "What are Filter Expressions and why do they matter?"

**One-liner:** "SQL-like filtering that V1 couldn't do. Game changer for everything, not just Processors."

**Full answer:**

V1 filtering was primitive:
- Filter by stream name OR event type (not both)
- Regex patterns
- No combinations
- No metadata access

V2 Filter Expressions give you SQL WHERE clause semantics:

```sql
stream LIKE 'order-%' AND 
properties.region IN ('us-east', 'us-west') AND 
properties.amount > 100 AND
timestamp BETWEEN '2024-01-01' AND '2024-12-31'
```

**Supported operators:**
- Comparison: `=`, `!=`, `<`, `>`, `<=`, `>=`
- Logical: `AND`, `OR`, `NOT`, parentheses
- Pattern: `LIKE`, `ILIKE`, `NOT LIKE`
- Set: `IN`, `NOT IN`
- Range: `BETWEEN`, `NOT BETWEEN`
- Null: `IS NULL`, `IS NOT NULL`

**DuckDB compatible syntax.** Currently evaluated client-side. Roadmap includes server-side optimization where the server filters before sending.

---

### Q: "What's the new terminology about? Records vs Events?"

**Answer:**

Deliberate shift in ubiquitous language:

| V1 Term | V2 Term | Why |
|---------|---------|-----|
| Event | **Record** | We store more than events—telemetry, commands, state |
| Event Type | **Schema Name** | Aligns with Schema Registry; messages have schemas |
| Payload | **Message** | Records contain messages; that's what matters |

KurrentDB is a **record store**. Events are one type of message. So are commands, telemetry, state snapshots. The V2 language reflects this.

---

### Q: "What else is new in Client V2?"

**Answer:**

| Feature | Description |
|---------|-------------|
| **Filter expressions** | SQL-like syntax, DuckDB compatible |
| **Auto SerDe** | JSON and Protobuf with Schema Registry, zero config |
| **Built-in resilience** | Connection resiliency embedded in client |
| **Type-safe errors** | Result pattern throughout—no exception-based flow |
| **Discriminated unions** | IVariant for exhaustive pattern matching |
| **Zero reflection** | Source generators for AOT compatibility |
| **Protocol versioning** | v1 (EventStoreDB compat) + v2 (KurrentDB native) |
| **Multi-stream append** | Simpler API than V1 |

---

### Q: "Why does the Gateway abstraction matter for Processors?"

**Answer:**

This is **Processors-specific architecture**, not a V2 feature. It's why Processors can ship without server changes.

The Gateway is a **Ports & Adapters** pattern we built for Processors:

```
Worker Code → IGateway interface → LegacyGateway (today)
                                 → GrpcGateway (future)
```

**LegacyGateway** simulates the entire processor protocol client-side using the V1 Streams API. Full checkpoint coordination, barrier handling, position tracking—all without touching the server.

**GrpcGateway** (future) uses the real server protocol for better performance.

**The magic:** Swap gateways, worker code unchanged. Users don't rewrite anything when server protocol ships.

We built this *using* V2's architectural patterns, but it's not something V2 provides out of the box.

---

## Category 2: "Why Not Just Use Flink?"

### Q: "This is basically Flink. Why reinvent the wheel?"

**One-liner:** "We borrowed their checkpointing, dropped the complexity our users don't need."

**Full answer:**

Yes, we use the Chandy-Lamport algorithm—the same foundation as Flink. That's intentional. It's battle-tested since 1985.

What we BORROWED from Flink:
| Concept | What it does |
|---------|--------------|
| Chandy-Lamport barriers | Consistent distributed snapshots |
| Exactly-once state | State reflects each record once |
| Savepoints | Manual checkpoints that persist |
| Key groups | Partitioning model for stateful work |

What we DROPPED:
| Flink has... | Why we don't need it |
|--------------|---------------------|
| Barrier alignment | Single source log, no multiple upstream channels |
| Operator DAGs | Users process records directly, no transformation chains |
| Watermarks / event time | $all log is already ordered |
| Standalone cluster | Embedded in KurrentDB |
| Job deployment model | Autonomous workers connect when ready |

**Key insight:** KurrentDB has a simpler topology. Flink handles arbitrary dataflow graphs with multiple inputs per operator. We have ONE source ($all log) feeding all workers. That eliminates most of Flink's complexity.

---

### Q: "Flink Reactive Mode already does autonomous workers. What's different?"

**Answer:**

You're right—Flink added Reactive Mode in 2021 for dynamic scaling. The difference:

| Flink Reactive Mode | Kurrent Processors |
|---------------------|-------------------|
| Opt-in mode on existing cluster | Default architecture |
| Still requires JobManager + TaskManagers | Embedded in KurrentDB |
| Workers scale, but cluster exists | No separate cluster |
| Deploy Flink first, then enable reactive | Just connect workers |

Reactive Mode is Flink's answer to "we want dynamic scaling." Our Hive Mind model is the entire architecture—not a mode you enable.

---

### Q: "Why not connect Flink to KurrentDB as a source?"

**Answer:**

You could. But then you're managing two systems:
- KurrentDB for your event store
- Flink cluster (JobManager + TaskManagers + ZooKeeper/High Availability)

Our users chose KurrentDB for simplicity. Adding a Flink cluster negates that. Kurrent Processors gives them reliable processing without infrastructure sprawl.

Also: Flink's learning curve is significant. Operator DAGs, parallelism configuration, state backends, savepoint management—all concepts your team needs to learn. Our model is simpler: connect workers, process records, checkpoints happen automatically.

---

## Category 3: "Is This Too Complex for V2?"

### Q: "Distributed coordination is hard. Are we ready for this?"

**One-liner:** "Single-worker is done. The distributed parts are Chandy-Lamport—1985, battle-tested."

**Full answer:**

The single-worker version is **complete and working**. That's not a prototype—it's the real implementation with:
- Full checkpoint lifecycle
- Position tracking per partition
- State snapshotting and recovery
- Fluent builder API
- LegacyGateway (no server changes needed)

The distributed coordination people worry about? That's the Chandy-Lamport algorithm. It's been proving correct distributed snapshots since 1985. We didn't invent new theory—we applied proven theory to KurrentDB's topology.

What makes our implementation simpler:
- **Single source log**: No barrier alignment needed
- **No operator DAGs**: Workers process records directly
- **Gateway abstraction**: Server changes are optional optimizations

---

### Q: "What's actually new code vs. proven patterns?"

**Answer:**

| Component | Source |
|-----------|--------|
| Checkpoint algorithm | Chandy-Lamport (1985) |
| Barrier protocol | Adapted from Flink |
| Partition distribution | Consistent hashing (standard) |
| Gateway abstraction | Ports & Adapters (standard) |
| Position tracking | New code, simple arithmetic |
| Worker state machine | New code, event-driven |
| Batch-then-barrier flow | New implementation of Chandy-Lamport requirement |

The hard parts (consistent distributed snapshots) are proven. The new parts (wiring for KurrentDB) are relatively straightforward.

---

### Q: "What if checkpointing fails?"

**Answer:**

Checkpointing can fail for several reasons. Each has a defined behavior:

**Worker disconnects mid-checkpoint:**
- Checkpoint times out (default 60s)
- Marked FAILED
- Depending on policy:
  - `Continue`: Disconnect failed worker, reassign partitions, resume from last successful checkpoint
  - `FailProcessor`: All workers disconnect, manual intervention needed

**Storage failure:**
- Worker retries with backoff
- If all retries fail → timeout → same as disconnect

**Barrier timeout:**
- Same as worker disconnect

**Critical guarantee:** No failure mode can cause inconsistent state. The Chandy-Lamport algorithm ensures checkpoints are either complete (all workers acked) or failed (discarded). There's no partial state.

---

### Q: "What about head-of-line blocking?"

**Answer:**

Valid concern. If one partition is slow, it blocks other partitions on the same gRPC stream.

**Solution: Client-Side Parallelism**

Run multiple workers in the same process:
```
Your Process
├── Worker A (stream 1) → Partitions [0-42]
├── Worker B (stream 2) → Partitions [43-85]
└── Worker C (stream 3) → Partitions [86-127]
```

Each worker has its own TCP connection. Slow partition on Worker A doesn't block Workers B or C.

The server doesn't know they're in the same process—it just sees 3 workers and coordinates barriers across all of them. This emerges naturally from the Hive Mind architecture.

---

## Category 4: "Who Asked for This?"

### Q: "Is this solving a real problem or building features nobody wanted?"

**One-liner:** "Everyone who built custom checkpointing, asked about scaling, or migrated away."

**Full answer:**

The requests are implicit in what users DO:

| What users do | What it reveals |
|---------------|-----------------|
| Build custom retry logic | Need built-in fault tolerance |
| Implement manual checkpointing | Need automatic checkpoints |
| Ask "how do I scale subscriptions?" | Need multi-worker coordination |
| Migrate to Kafka/Flink/Databricks | Processing is too hard on KurrentDB |
| Write custom frameworks | Our primitives are too low-level |

The subscription API is a low-level primitive. Every team that uses it builds their own mini-framework on top. That's wasted effort and bug surface area.

---

### Q: "Can you name specific customers who wanted this?"

**Answer:**

[Insert actual customer examples here if available]

Beyond specific names: every subscription-related support ticket, every "how do I checkpoint?" in Discord, every "how do I scale?" forum post—those are implicit requests.

We've had customers leave for Databricks and ClickHouse specifically because processing on KurrentDB required too much custom work.

---

### Q: "What use cases does this enable?"

**Answer:**

| Use case | Description |
|----------|-------------|
| **Projections** | Maintain read models from event streams |
| **Process managers** | Coordinate sagas across services |
| **Event handlers** | Trigger side effects reliably |
| **Analytics aggregators** | Compute real-time metrics |
| **Replication handlers** | Sync to external systems |
| **Audit processors** | Track changes with guaranteed delivery |

All of these exist today—built on fragile, custom foundations. Processors provides the reliable base.

---

## Category 5: Technical Deep-Dive

### Q: "Explain Chandy-Lamport in simple terms"

**Answer:**

Imagine photographing a distributed system at a specific moment. You can't freeze everything—messages are in flight, processes are running.

Chandy-Lamport's insight: you don't need to freeze. Send a special marker (barrier) through the system:

1. When a process sees the marker, it snapshots its state
2. It forwards the marker to its outputs
3. It reports back to the coordinator
4. When all processes have reported → consistent snapshot

The snapshot isn't of a frozen moment—it's of a "cut" through the system where no message is lost or duplicated.

**For KurrentDB:**
- Barrier flows through the log
- Workers snapshot when they see it
- Coordinator waits for all acks
- Done

---

### Q: "Why don't we need barrier alignment?"

**Answer:**

Barrier alignment solves this problem:

```
Worker W receives data from Sources A and B.
A sends barrier at position 100.
B hasn't sent its barrier yet.
Should W checkpoint?

If W checkpoints now, it captures A's state at 100
but B's state at "wherever B is." Inconsistent.
```

Flink's solution: W buffers data from A until B's barrier arrives, then checkpoints with both barriers aligned.

**KurrentDB has ONE source:** the $all log. Every worker reads from the same ordered log. There's nothing to align. When the barrier arrives, checkpoint. Simple.

---

### Q: "What's the difference between barrier position and worker position?"

**Answer:**

This is a common confusion point. They're different things:

**Barrier position:** The logical stopping point. "Everyone checkpoint at position 1000."

**Worker position:** The last record each worker actually processed. Due to partitioning and filtering, each worker processes different records.

Example:
```
Log positions:    1    2    3    4    5    6    7    8    9    10
Partition keys:   A    B    A    B    A    B    A    B    A    B

Worker 1 (A): processes 1, 3, 5, 7, 9  → reports position 9
Worker 2 (B): processes 2, 4, 6, 8, 10 → reports position 10

Barrier position: 10
Worker positions: 9 and 10 (both correct)
```

Recovery uses the **barrier position** because that's the consistent cut. Worker positions are metadata for debugging.

---

### Q: "What guarantees does exactly-once state give us?"

**Answer:**

**What it guarantees:**
State transformations are applied exactly once. If you increment a counter, it increments once per record—even across failures and restarts.

**What it doesn't guarantee:**
Side effects to external systems. If your handler sends an email, we can't un-send it.

**Worker responsibility:**
Idempotency for external calls (deduplication keys, conditional writes, etc.)

This is the same guarantee Flink provides. "Exactly-once state" ≠ "exactly-once delivery to external systems."

---

### Q: "How does batching interact with checkpoints?"

**Answer:**

The server batches records for efficiency (don't send one record at a time over gRPC).

When checkpoint triggers mid-batch:
1. **Flush all pending batches** (send whatever's buffered)
2. **Send Barrier to all workers**
3. Resume normal batching

Workers never see a checkpoint mid-batch. They process complete batches, then see the barrier.

This is the Chandy-Lamport requirement: markers (barriers) travel through the same channels as data, maintaining causality.

---

### Q: "What's the checkpoint overhead?"

**Answer:**

| Operation | Cost |
|-----------|------|
| Barrier propagation | One message per worker (negligible) |
| State snapshot | Depends on state size. MB range = milliseconds |
| Storage write | One write per worker to checkpoint storage |

For typical use cases (read-model projections, process managers):
- State is MB range
- Checkpoint time is milliseconds to low seconds
- Unnoticeable in normal operation

For GB+ state stores: we'll need incremental checkpoints (on roadmap).

---

## Category 6: Roadmap Questions

### Q: "When will multi-worker be ready?"

**Answer:**

2-3 weeks of focused work. The design is complete. The LegacyGateway can simulate multi-worker coordination without server changes.

What's needed:
- Partition reassignment logic
- Rebalance checkpoint triggering
- State redistribution flow
- Testing at scale

---

### Q: "What's Autonomous Mode?"

**Answer:**

**Important clarification:** Records always come from KurrentDB—that's the product. Autonomous Mode is about where *coordination* lives.

**Today (with server protocol):** Workers coordinate through KurrentDB server. The server manages partition assignments, checkpoint coordination, worker discovery.

**Autonomous Mode:** Workers coordinate through a distributed state store (Redis, DuckDB, etcd, etc.). They form a peer-to-peer mesh. KurrentDB serves records; State Store handles coordination.

```
Records:      KurrentDB → Workers (always, this is the product)
Coordination: State Store ↔ Workers (checkpoints, assignments, discovery)
```

**Why is this possible?** The pluggable State Store abstraction. All coordination state—checkpoints, partition assignments, worker registry—can live in any store that implements the interface.

Why it matters:
- No server protocol dependency for multi-worker
- True elastic scaling without coordinator bottleneck
- Workers can run anywhere
- Coordination survives KurrentDB restarts

---

### Q: "How far is Autonomous Mode?"

**Answer:**

~2 weeks after multi-worker is complete. The architecture already supports this—it's implementation, not design work.

We need:
1. Define distributed state store interface
2. Implement worker discovery through store
3. Implement partition assignment through store
4. Implement checkpoint coordination through store

The State Store abstraction is what makes this possible. The same interface that stores checkpoints today can store coordination state tomorrow.

---

### Q: "What's the server protocol dependency?"

**Answer:**

| Phase | Server changes? |
|-------|----------------|
| Phase 1 (single-worker) | None. LegacyGateway. |
| Phase 2 (multi-worker) | None. LegacyGateway can simulate. |
| Phase 3 (optimized) | GrpcGateway uses real protocol. Better perf. |

Server protocol is an optimization, not a blocker. We can ship everything without it.

---

## Category 7: Positioning Questions

### Q: "How does this compare to MassTransit/NServiceBus?"

**Answer:**

MassTransit/NServiceBus are message bus frameworks. They handle:
- Routing and topology
- Serialization
- Delivery guarantees
- Sagas and state machines

Kurrent Processors is stream processing infrastructure. We handle:
- Reading from the log
- Distributing work across workers
- Checkpointing and recovery
- State management

You could use MassTransit ON TOP of Processors (for message routing patterns) while Processors handles reliability.

---

### Q: "Is this competing with Kafka Streams?"

**Answer:**

Different positioning:
- **Kafka Streams:** Stream processing for Kafka users
- **Kurrent Processors:** Stream processing for KurrentDB users

Both embed processing in their respective platforms. Kafka Streams users aren't our audience—KurrentDB users who might migrate to Kafka are.

---

### Q: "Should we tell users to use this instead of raw subscriptions?"

**Answer:**

Not instead—in addition. Raw subscriptions still have their place:
- Simple consumption patterns
- Maximum control needed
- Learning/prototyping

Recommendation ladder:
1. **Learning:** Raw subscriptions
2. **Prototyping:** Raw subscriptions or simple Processor
3. **Production:** Processors (checkpointing, recovery, scaling)

---

## Quick Reference Card

| Challenge | One-liner |
|-----------|-----------|
| "What's V2?" | "Complete SDK reimagining—Processors is the flagship feature." |
| "Filter expressions?" | "SQL-like syntax. DuckDB compatible. V1 couldn't combine filters." |
| "Records vs events?" | "Records store anything—events, commands, telemetry. New language." |
| "Why not Flink?" | "Borrowed checkpointing, dropped complexity we don't need." |
| "Too complex?" | "Single-worker done. Distributed parts are Chandy-Lamport—1985." |
| "Who asked?" | "Everyone who built custom checkpointing or migrated away." |
| "Barrier alignment?" | "Single source log. Nothing to align." |
| "Exactly-once?" | "State transformations. External side effects are yours." |
| "Timeline?" | "Single-worker: done. Multi-worker: 2-3 weeks. Autonomous: 2 weeks after." |
| "Server changes?" | "None required. LegacyGateway works without them." |
| "Gateway abstraction?" | "Ports & Adapters. Swap implementations, zero code changes." |
| "Autonomous Mode?" | "Records from KurrentDB (always). Coordination via State Store (no server needed)." |

---

*Keep this open during Q&A. Confidence comes from preparation.*
